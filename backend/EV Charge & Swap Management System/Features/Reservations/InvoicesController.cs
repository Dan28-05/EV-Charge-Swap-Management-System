using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EVChargeSwap.Data;
using EVChargeSwap.Features.Authorization;
using EVChargeSwap.Features.Reservations.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EVChargeSwap.Features.Reservations;

public sealed record InvoiceDetailResponse(
    Guid Id,
    Guid ReservationId,
    string? ReservationCode,
    Guid CustomerId,
    decimal TotalKwh,
    decimal KwhRate,
    decimal EnergyAmount,
    decimal TotalAmount,
    string Status,
    string? PaymentReference,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PaidAtUtc,
    decimal? SwapRate);

[ApiController]
[Authorize]
[Route("api/invoices")]
public sealed class InvoicesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceDetailResponse>>> GetInvoices(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var isStaff = User.IsInRole(AppRoles.Admin);

        var query = from invoice in db.Invoices.AsNoTracking()
                    join reservation in db.Reservations.AsNoTracking()
                        on invoice.ReservationId equals reservation.Id into resGroup
                    from res in resGroup.DefaultIfEmpty()
                    where isStaff || invoice.CustomerId == userId
                    select new InvoiceDetailResponse(
                        invoice.Id,
                        invoice.ReservationId,
                        res != null ? res.Code : null,
                        invoice.CustomerId,
                        invoice.TotalKwh,
                        invoice.KwhRate,
                        invoice.EnergyAmount,
                        invoice.TotalAmount,
                        invoice.Status.ToString(),
                        invoice.PaymentReference,
                        invoice.CreatedAtUtc,
                        invoice.PaidAtUtc,
                        invoice.SwapRate);

        var rawItems = await query.Take(100).ToListAsync(ct);
        var items = rawItems.OrderByDescending(x => x.CreatedAtUtc).ToList();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceDetailResponse>> GetInvoice(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var isStaff = User.IsInRole(AppRoles.Admin);

        var query = from invoice in db.Invoices.AsNoTracking()
                    join reservation in db.Reservations.AsNoTracking()
                        on invoice.ReservationId equals reservation.Id into resGroup
                    from res in resGroup.DefaultIfEmpty()
                    where invoice.Id == id && (isStaff || invoice.CustomerId == userId)
                    select new InvoiceDetailResponse(
                        invoice.Id,
                        invoice.ReservationId,
                        res != null ? res.Code : null,
                        invoice.CustomerId,
                        invoice.TotalKwh,
                        invoice.KwhRate,
                        invoice.EnergyAmount,
                        invoice.TotalAmount,
                        invoice.Status.ToString(),
                        invoice.PaymentReference,
                        invoice.CreatedAtUtc,
                        invoice.PaidAtUtc,
                        invoice.SwapRate);

        var item = await query.SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    private bool TryGetUserId(out Guid id) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out id);
}
