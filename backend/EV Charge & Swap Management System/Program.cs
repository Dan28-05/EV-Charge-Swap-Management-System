using EVChargeSwap.Data;
using EVChargeSwap.Features.Authentication;
using EVChargeSwap.Features.Authentication.Services;
using EVChargeSwap.Features.Locations.Caching;
using EVChargeSwap.Features.Locations.Hubs;
using EVChargeSwap.Features.Locations.Services;
using EVChargeSwap.Features.Users.Entities;
using EVChargeSwap.Features.Users.Services;
using EVChargeSwap.Common.Errors;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Consumers;
using EVChargeSwap.Health;
using EVChargeSwap.Features.Locations.Search;
using EVChargeSwap.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("EVChargeSwap.Api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(RabbitMqConsumersHostedService.ActivitySource.Name)
            .AddAspNetCoreInstrumentation(options => options.RecordException = true)
            .AddHttpClientInstrumentation()
            .AddNpgsql();
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
            tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("EVChargeSwap.LocationSearch", "EVChargeSwap.RedisCache")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsqlInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();
        if (!string.IsNullOrWhiteSpace(otelEndpoint))
            metrics.AddOtlpExporter();
    });

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    const string bearerSchemeId = "Bearer";

    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();
        document.Components.SecuritySchemes[bearerSchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };
        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = bearerSchemeId
                    }
                }] = Array.Empty<string>()
            });
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.AddOptions<JwtOptions>()
    .Bind(jwtSection)
    .ValidateDataAnnotations()
    .ValidateOnStart();
var jwtOptions = jwtSection.Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configure the Jwt section.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32)
    throw new InvalidOperationException("Set Jwt:SigningKey to a secret containing at least 32 characters.");

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Set ConnectionStrings:Default with user-secrets or an environment variable.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var options = ConfigurationOptions.Parse(redisConn);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 3000;
        options.SyncTimeout = 2000;
        options.AsyncTimeout = 2000;
        return ConnectionMultiplexer.Connect(options);
    });
}

builder.Services.AddSingleton<ILocationCache>(sp =>
{
    var multiplexer = sp.GetService<IConnectionMultiplexer>();
    var logger = sp.GetRequiredService<ILogger<RedisLocationCache>>();
    return new RedisLocationCache(multiplexer, logger);
});
builder.Services.AddScoped<LocationService>();
builder.Services.AddOptions<LocationSearchOptions>()
    .BindConfiguration(LocationSearchOptions.SectionName)
    .Validate(options => ElasticLocationIndex.IsValidAlias(options.Alias),
        "LocationSearch:Alias must be a lowercase Elasticsearch alias.")
    .Validate(options => !options.Enabled ||
        Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri) && uri.Host.Length > 0 &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Scheme == Uri.UriSchemeHttps || builder.Environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp),
        "LocationSearch:Endpoint must use HTTPS outside Development.")
    .Validate(options => !options.Enabled || builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.ApiKey),
        "LocationSearch:ApiKey must be configured outside Development.")
    .ValidateOnStart();
builder.Services.AddSingleton<ElasticLocationIndex>();
builder.Services.AddScoped<LocationSearchService>();
builder.Services.AddScoped<LocationIndexer>();
builder.Services.AddHostedService<LocationSearchInitializer>();

var signalR = builder.Services.AddSignalR();
if (builder.Configuration.GetValue("SignalR:UseRedisBackplane", false) && !string.IsNullOrWhiteSpace(redisConn))
{
    signalR.AddStackExchangeRedis(redisConn, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("evcs-signalr");
        options.Configuration.AbortOnConnectFail = false;
        options.Configuration.ConnectTimeout = 3000;
        options.Configuration.SyncTimeout = 2000;
        options.Configuration.AsyncTimeout = 2000;
    });
}
builder.Services.AddSingleton<IStationNotifier, StationNotifier>();
builder.Services.AddScoped<EVChargeSwap.Features.Reservations.Services.ReservationService>();
builder.Services.AddScoped<EVChargeSwap.Features.Reservations.Services.ChargingService>();
builder.Services.AddScoped<EVChargeSwap.Features.Reservations.Services.OperatorAssignmentService>();
builder.Services.AddHostedService<EVChargeSwap.Features.Reservations.Services.ReservationExpiryJob>();

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
builder.Services.AddScoped<IOutboxService, OutboxService>();
builder.Services.AddScoped<EVChargeSwap.Features.Reservations.Services.IncidentService>();
builder.Services.AddScoped<IInboxService, InboxService>();
builder.Services.AddScoped<NotificationConsumer>();
builder.Services.AddScoped<EmailDeliveryConsumer>();
builder.Services.AddScoped<BillingConsumer>();
builder.Services.AddScoped<InvoiceRealtimeConsumer>();
builder.Services.AddScoped<EVChargeSwap.Features.Reservations.Services.InvoiceRealtime>();
builder.Services.AddScoped<ReservationRealtimeConsumer>();
builder.Services.AddScoped<LocationUpdatedConsumer>();
builder.Services.AddHostedService<OutboxPublisherHostedService>();
builder.Services.AddHostedService<RabbitMqConsumersHostedService>();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path == "/hubs/notifications" &&
                    context.Request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
                    context.Token = token[0];
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                var tokenStamp = context.Principal?.FindFirst("security_stamp")?.Value;
                if (!Guid.TryParse(subject, out var userId))
                {
                    context.Fail("The token subject is invalid.");
                    return;
                }

                var userManager = context.HttpContext.RequestServices
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var user = await userManager.FindByIdAsync(userId.ToString());
                if (user is null || !user.IsActive ||
                    !string.Equals(user.SecurityStamp, tokenStamp, StringComparison.Ordinal))
                    context.Fail("The account is unavailable or the token has been revoked.");
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy, out var ip))
            options.KnownProxies.Add(ip);
    }
    foreach (var cidr in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = cidr.Split('/');
        if (parts.Length == 2 && IPAddress.TryParse(parts[0], out var prefix) && int.TryParse(parts[1], out var length))
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Request.Path.Value?.ToLowerInvariant()}:{context.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("webhook", context => RateLimitPartition.GetFixedWindowLimiter(
        $"webhook:{context.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<VerificationService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.Configure<VerificationDeliveryOptions>(
    builder.Configuration.GetSection(VerificationDeliveryOptions.SectionName));
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<DevelopmentCodeInbox>();
    builder.Services.AddSingleton<IVerificationCodeSender>(sp => sp.GetRequiredService<DevelopmentCodeInbox>());
}
else
{
    builder.Services.AddHttpClient<IVerificationCodeSender, ProductionVerificationCodeSender>()
        .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
}
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AdminBootstrapper>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(
    "database", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Angular", policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "EV Charge & Swap API";
        options.SwaggerEndpoint("/openapi/v1.json", "EV Charge & Swap API v1");
    });
}

if (builder.Configuration.GetValue("HttpsRedirection:Enabled", true))
{
    app.UseHttpsRedirection();
}
app.UseCors("Angular");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapPrometheusScrapingEndpoint("/metrics");
app.MapControllers();
app.MapHub<StationHub>("/hubs/stations");
app.MapHub<EVChargeSwap.Features.Users.Hubs.NotificationHub>("/hubs/notifications",
    options => options.CloseOnAuthenticationExpiration = true);

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().InitializeAsync();
}

app.Run();

public partial class Program { }
