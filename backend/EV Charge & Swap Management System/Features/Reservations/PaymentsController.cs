using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Common.Messaging.Events;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record MockPaymentEvent(Guid EventId, Guid PaymentId, Guid InvoiceId, decimal Amount, bool Succeeded);
public sealed record MockPaymentResult(bool Succeeded);

[ApiController]
[Authorize(Roles = AppRoles.Customer)]
public sealed class PaymentsController(AppDbContext db, TimeProvider clock, IOutboxService outbox,
    IConfiguration configuration, IWebHostEnvironment environment) : ControllerBase
{
    private string? SigningKey => configuration["MockPayments:WebhookSecret"] is { Length: >= 32 } key &&
        environment.IsDevelopment() ? key : null;

    [HttpPost("api/invoices/{invoiceId:guid}/pay")]
    public async Task<IActionResult> Create(Guid invoiceId, CancellationToken ct)
    {
        if (SigningKey is null) return Unavailable();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await LockInvoiceAsync(invoiceId, ct);
        if (invoice is null || invoice.CustomerId != UserId()) return NotFound();
        if (invoice.Status != InvoiceStatus.Pending)
            return Problem(statusCode: 409, detail: "Invoice is not payable.");

        var current = await db.Payments.SingleOrDefaultAsync(x => x.InvoiceId == invoiceId &&
            x.Status == PaymentStatus.Pending, ct);
        if (current is not null) return Ok(current);

        var payment = new Payment { InvoiceId = invoiceId, Amount = invoice.TotalAmount, CreatedAtUtc = clock.GetUtcNow() };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(201, payment);
    }

    [HttpPost("api/payments/{id:guid}/mock-result")]
    public async Task<IActionResult> Simulate(Guid id, MockPaymentResult request, CancellationToken ct)
    {
        if (SigningKey is not { } key) return Unavailable();
        var payment = await (from p in db.Payments.AsNoTracking()
            join i in db.Invoices on p.InvoiceId equals i.Id
            where p.Id == id && i.CustomerId == UserId()
            select p).SingleOrDefaultAsync(ct);
        if (payment is null) return NotFound();
        var evt = new MockPaymentEvent(Guid.NewGuid(), id, payment.InvoiceId, payment.Amount, request.Succeeded);
        return await AcceptWebhookAsync(evt, Sign(evt, key), key, ct);
    }

    [AllowAnonymous]
    [HttpPost("api/payments/mock/webhook")]
    public Task<IActionResult> Webhook(MockPaymentEvent evt,
        [FromHeader(Name = "X-EVGo-Signature")] string? signature, CancellationToken ct) =>
        SigningKey is { } key ? AcceptWebhookAsync(evt, signature, key, ct)
            : Task.FromResult<IActionResult>(Unavailable());

    private async Task<IActionResult> AcceptWebhookAsync(MockPaymentEvent evt, string? signature, string key,
        CancellationToken ct)
    {
        if (evt.EventId == Guid.Empty || evt.PaymentId == Guid.Empty || evt.InvoiceId == Guid.Empty ||
            evt.Amount < 0 || decimal.Round(evt.Amount, 2) != evt.Amount || evt.Amount >= 10_000_000_000_000m)
            return Problem(statusCode: 400, detail: "Invalid payment event.");
        if (!IsAuthenticated(evt, signature, key)) return Unauthorized();

        var payloadHash = Convert.ToHexString(SHA256.HashData(Payload(evt)));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var eventKey = $"payment-event:{evt.EventId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({eventKey}, 0))", ct);
        var prior = await db.PaymentWebhookEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == evt.EventId, ct);
        if (prior is not null)
            return prior.PayloadHash == payloadHash ? NoContent() : Problem(statusCode: 409, detail: "Event ID conflict.");

        var invoice = await LockInvoiceAsync(evt.InvoiceId, ct);
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == evt.PaymentId &&
            x.InvoiceId == evt.InvoiceId, ct);
        if (payment is null || invoice is null || payment.Amount != evt.Amount || invoice.TotalAmount != evt.Amount)
            return Problem(statusCode: 409, detail: "Payment does not match the invoice and amount.");

        var requested = evt.Succeeded ? PaymentStatus.Succeeded : PaymentStatus.Failed;
        if (payment.Status != PaymentStatus.Pending && payment.Status != requested ||
            payment.Status == PaymentStatus.Pending && invoice.Status != InvoiceStatus.Pending)
            return Problem(statusCode: 409, detail: "The payment cannot change to the requested state.");

        var now = clock.GetUtcNow();
        if (payment.Status == PaymentStatus.Pending)
        {
            payment.Status = requested;
            payment.FinishedAtUtc = now;
            if (requested == PaymentStatus.Succeeded)
            {
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAtUtc = now;
                invoice.PaymentReference = $"MOCK-{payment.Id:N}";
                await outbox.EnqueueAsync("PaymentSucceeded", EventRoutingKeys.PaymentSucceeded,
                    new PaymentSucceededEvent(invoice.CustomerId, invoice.Id), ct);
            }
        }

        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = evt.EventId, PaymentId = payment.Id, PayloadHash = payloadHash, ReceivedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    public static string Sign(MockPaymentEvent evt, string key) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Payload(evt)));

    private static bool IsAuthenticated(MockPaymentEvent evt, string? signature, string key)
    {
        if (signature is not { Length: 64 }) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Payload(evt)));
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Payload(MockPaymentEvent evt) => Encoding.UTF8.GetBytes(
        $"{evt.EventId:N}|{evt.PaymentId:N}|{evt.InvoiceId:N}|{evt.Amount.ToString("F2", CultureInfo.InvariantCulture)}|{(evt.Succeeded ? 1 : 0)}");

    private Task<Invoice?> LockInvoiceAsync(Guid id, CancellationToken ct) =>
        db.Invoices.FromSqlInterpolated($"""SELECT * FROM invoices WHERE "Id" = {id} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);

    private Guid UserId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private ObjectResult Unavailable() =>
        Problem(statusCode: 503, detail: "Mock payments require Development and a configured webhook secret.");
}
