using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record InvoiceDetailResponse(
    Guid Id, Guid ReservationId, string? ReservationCode, Guid CustomerId, decimal TotalKwh,
    decimal KwhRate, decimal EnergyAmount, decimal TotalAmount, string Status, string? PaymentReference,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? PaidAtUtc, decimal? SwapRate,
    DateTimeOffset? DueAtUtc, string? PaymentMethod, Guid? CollectedByOperatorId,
    string? CustomerName = null, string? CustomerPhone = null, string? CustomerEmail = null,
    string? CollectorName = null);

[ApiController]
[Authorize]
[Route("api/invoices")]
public sealed class InvoicesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceDetailResponse>>> GetInvoices(CancellationToken ct, [FromQuery] int page = 1)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        const int pageSize = 100;
        if (page < 1 || page > int.MaxValue / pageSize) return BadRequest();
        var offset = (page - 1) * pageSize;
        var query = InvoiceDetails(userId, offset: db.Database.IsNpgsql() ? offset : null, pageSize: pageSize);
        var items = db.Database.IsNpgsql()
            ? await query.ToListAsync(ct)
            : (await query.ToListAsync(ct)).OrderByDescending(x => x.CreatedAtUtc)
                .ThenByDescending(x => x.Id).Skip(offset).Take(pageSize).ToList();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDetailResponse>> GetInvoice(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var item = await InvoiceDetails(userId, id).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    private IQueryable<InvoiceDetailResponse> InvoiceDetails(Guid userId, Guid? id = null, int? offset = null, int pageSize = 100)
    {
        var isAdmin = User.IsInRole(AppRoles.Admin);
        var isOperator = User.IsInRole(AppRoles.Operator);
        var rows = from invoice in db.Invoices.AsNoTracking()
               join reservation in db.Reservations.AsNoTracking()
                   on invoice.ReservationId equals reservation.Id into reservations
               from reservation in reservations.DefaultIfEmpty()
               join customer in db.Users.AsNoTracking()
                   on invoice.CustomerId equals customer.Id into customers
               from customer in customers.DefaultIfEmpty()
               join payment in db.Payments.AsNoTracking().Where(x => x.Status == PaymentStatus.Succeeded)
                   on invoice.Id equals payment.InvoiceId into payments
               from payment in payments.DefaultIfEmpty()
               join collector in db.Users.AsNoTracking()
                   on payment.CollectedByOperatorId equals collector.Id into collectors
               from collector in collectors.DefaultIfEmpty()
               where (id == null || invoice.Id == id) &&
                   (isAdmin || invoice.CustomerId == userId || (isOperator && reservation != null &&
                   db.Set<OperatorAssignment>().Any(a => a.OperatorId == userId &&
                       a.LocationId == reservation.LocationId && a.EndedAtUtc == null)))
               select new { invoice, reservation, customer, payment, collector };

        if (offset is not null)
            rows = rows.OrderByDescending(x => x.invoice.CreatedAtUtc).ThenByDescending(x => x.invoice.Id)
                .Skip(offset.Value).Take(pageSize);

        return from row in rows
               let invoice = row.invoice
               let reservation = row.reservation
               let customer = row.customer
               let payment = row.payment
               let collector = row.collector
               select new InvoiceDetailResponse(
                   invoice.Id, invoice.ReservationId, reservation != null ? reservation.Code : null,
                   invoice.CustomerId, invoice.TotalKwh, invoice.KwhRate, invoice.EnergyAmount,
                   invoice.TotalAmount, invoice.Status.ToString(), invoice.PaymentReference,
                   invoice.CreatedAtUtc, invoice.PaidAtUtc, invoice.SwapRate, invoice.DueAtUtc,
                   payment != null ? (payment.CollectedByOperatorId.HasValue ? "Cash" : "MockQr") : null,
                   isOperator || isAdmin ? payment.CollectedByOperatorId : null,
                   customer != null ? (!string.IsNullOrWhiteSpace(customer.FullName) ? customer.FullName : customer.Email) : null,
                   customer != null ? customer.PhoneNumber : null,
                   customer != null ? customer.Email : null,
                   (isOperator || isAdmin) && collector != null
                       ? (!string.IsNullOrWhiteSpace(collector.FullName) ? collector.FullName : collector.Email)
                       : null);
    }

    private bool TryGetUserId(out Guid id) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out id);
}
