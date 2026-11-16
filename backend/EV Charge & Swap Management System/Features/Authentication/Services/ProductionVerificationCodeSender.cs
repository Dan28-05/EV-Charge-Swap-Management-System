using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Options;

namespace EVChargeSwap.Features.Authentication.Services;

public sealed class ProductionVerificationCodeSender(
    HttpClient httpClient,
    IOptions<VerificationDeliveryOptions> options,
    ILogger<ProductionVerificationCodeSender> logger) : IVerificationCodeSender
{
    private readonly VerificationDeliveryOptions settings = options.Value;

    public bool CanSend(string channel) => channel switch
    {
        "email" => !string.IsNullOrWhiteSpace(settings.ResendApiKey) &&
                   !string.IsNullOrWhiteSpace(settings.EmailFrom),
        "phone" => !string.IsNullOrWhiteSpace(settings.TwilioAccountSid) &&
                   !string.IsNullOrWhiteSpace(settings.TwilioAuthToken) &&
                   !string.IsNullOrWhiteSpace(settings.TwilioSenderId),
        _ => false
    };

    public async Task SendAsync(string channel, string destination, string code,
        CancellationToken cancellationToken)
    {
        if (!CanSend(channel))
            throw new InvalidOperationException($"Verification delivery for {channel} is not configured.");

        if (channel == "email")
            await SendEmailAsync(destination, code, cancellationToken);
        else
            await SendSmsAsync(destination, code, cancellationToken);
    }

    private async Task SendEmailAsync(string destination, string code, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new
            {
                from = settings.EmailFrom,
                to = new[] { destination },
                subject = "Mã xác thực tài khoản EVGo",
                text = $"Mã xác thực EVGo của bạn là {code}. Mã có hiệu lực trong 5 phút. Không chia sẻ mã này với bất kỳ ai."
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "email", cancellationToken);
    }

    private async Task SendSmsAsync(string destination, string code, CancellationToken cancellationToken)
    {
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.TwilioAccountSid}:{settings.TwilioAuthToken}"));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{settings.TwilioAccountSid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = destination,
                ["From"] = settings.TwilioSenderId,
                ["Body"] = $"[EVGo] Ma xac thuc cua ban la {code}. Ma co hieu luc 5 phut. Khong chia se ma nay."
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "phone", cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string channel,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        logger.LogError("Verification provider rejected {Channel} delivery with status {StatusCode}.",
            channel, (int)response.StatusCode);
        throw new HttpRequestException($"The {channel} verification provider rejected the request.",
            null, response.StatusCode);
    }
}
