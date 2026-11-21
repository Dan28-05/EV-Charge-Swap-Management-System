using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using EVChargeSwap.Common.Messaging;
using EVChargeSwap.Features.Reservations.Services;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record MockPaymentEvent(Guid EventId, Guid PaymentId, Guid InvoiceId, decimal Amount, bool Succeeded);
public sealed record MockPaymentResult(bool Succeeded, decimal? Amount = null);
public sealed record QrPaymentResponse(Guid Id, Guid InvoiceId, decimal Amount, PaymentStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? FinishedAtUtc, string QrPayload);

[ApiController]
[Authorize(Roles = $"{AppRoles.Customer},{AppRoles.Operator},{AppRoles.Admin}")]
public sealed class PaymentsController(AppDbContext db, TimeProvider clock, IOutboxService outbox,
    IConfiguration configuration, IWebHostEnvironment environment, InvoiceRealtime? realtime = null) : ControllerBase
{
    private string? SigningKey => environment.IsDevelopment()
        ? (configuration["MockPayments:WebhookSecret"] is { Length: >= 32 } key
            ? key
            : "evgo-mock-payments-dev-webhook-secret-min-32chars")
        : (configuration["MockPayments:WebhookSecret"] is { Length: >= 32 } prodKey ? prodKey : null);

    [HttpPost("api/invoices/{invoiceId:guid}/pay")]
    public async Task<IActionResult> Create(Guid invoiceId, CancellationToken ct)
    {
        if (SigningKey is null) return Unavailable();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await Checkout.LockInvoiceAsync(db, invoiceId, ct);
        if (invoice is null || !await CanAccessAsync(invoice, ct)) return NotFound();
        if (invoice.Status != InvoiceStatus.Pending)
            return Problem(statusCode: 409, detail: "Invoice is not payable.");

        var current = await db.Payments.SingleOrDefaultAsync(x => x.InvoiceId == invoiceId &&
            x.Status == PaymentStatus.Pending, ct);
        if (current is not null) return Ok(ToQrResponse(current));

        var payment = new Payment { InvoiceId = invoiceId, Amount = invoice.TotalAmount, CreatedAtUtc = clock.GetUtcNow() };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(201, ToQrResponse(payment));
    }

    [HttpPost("api/payments/{id:guid}/mock-result")]
    public async Task<IActionResult> Simulate(Guid id, MockPaymentResult request, CancellationToken ct)
    {
        if (SigningKey is not { } key) return Unavailable();
        var attempt = await (from payment in db.Payments.AsNoTracking()
            join invoice in db.Invoices.AsNoTracking() on payment.InvoiceId equals invoice.Id
            where payment.Id == id && payment.CollectedByOperatorId == null
            select new { Payment = payment, Invoice = invoice }).SingleOrDefaultAsync(ct);
        if (attempt is null || !await CanAccessAsync(attempt.Invoice, ct)) return NotFound();
        var simulatedPayment = attempt.Payment;
        var evt = new MockPaymentEvent(Guid.NewGuid(), id, simulatedPayment.InvoiceId, request.Amount ?? simulatedPayment.Amount, request.Succeeded);
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

        var invoice = await Checkout.LockInvoiceAsync(db, evt.InvoiceId, ct);
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == evt.PaymentId &&
            x.InvoiceId == evt.InvoiceId, ct);
        if (payment is null || invoice is null || payment.CollectedByOperatorId.HasValue)
            return Problem(statusCode: 409, detail: "Payment does not match the invoice.");

        var wrongAmount = payment.Amount != evt.Amount || invoice.TotalAmount != evt.Amount;
        var requested = evt.Succeeded && !wrongAmount ? PaymentStatus.Succeeded : PaymentStatus.Failed;
        if (payment.Status != PaymentStatus.Pending && payment.Status != requested ||
            payment.Status == PaymentStatus.Pending && invoice.Status != InvoiceStatus.Pending)
            return Problem(statusCode: 409, detail: "The payment cannot change to the requested state.");

        var now = clock.GetUtcNow();
        if (payment.Status == PaymentStatus.Pending)
        {
            payment.Status = requested;
            payment.FinishedAtUtc = now;
            if (requested == PaymentStatus.Succeeded)
                await Checkout.SettleAsync(invoice, payment, now, outbox, ct);
        }

        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = evt.EventId, PaymentId = payment.Id, PayloadHash = payloadHash, ReceivedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (requested == PaymentStatus.Succeeded && realtime is not null)
            await realtime.TryNotifyAsync(invoice.Id);
        return wrongAmount ? Problem(statusCode: 409, detail: evt.Amount < invoice.TotalAmount
            ? "Số tiền thấp hơn số tiền hóa đơn." : "Số tiền vượt quá số tiền hóa đơn.") : NoContent();
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

    private static QrPaymentResponse ToQrResponse(Payment payment) =>
        new(payment.Id, payment.InvoiceId, payment.Amount, payment.Status,
            payment.CreatedAtUtc, payment.FinishedAtUtc, MockVietQr.Encode(payment));

    private async Task<bool> CanAccessAsync(Invoice invoice, CancellationToken ct)
    {
        if (User.IsInRole(AppRoles.Admin)) return true;
        if (!TryGetUserId(out var userId)) return false;
        if (invoice.CustomerId == userId) return true;
        return User.IsInRole(AppRoles.Operator) && await db.Reservations.AnyAsync(r =>
            r.Id == invoice.ReservationId && db.Set<OperatorAssignment>().Any(a =>
                a.OperatorId == userId && a.LocationId == r.LocationId && a.EndedAtUtc == null), ct);
    }

    private bool TryGetUserId(out Guid id) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out id);

    private ObjectResult Unavailable() =>
        Problem(statusCode: 503, detail: "Mock payments require Development and a configured webhook secret.");
}
