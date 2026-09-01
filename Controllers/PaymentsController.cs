using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Authorization;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Models;
using SocietyKhata.Api.Services;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class PaymentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.PaymentsView)]
    public async Task<ActionResult<List<PaymentDto>>> List()
    {
        var tenantId = User.GetTenantId();
        var items = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.Property)
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.PaymentsCreate)]
    public async Task<ActionResult<PaymentDto>> Create(PaymentRequest req)
    {
        var payment = Map(new Payment { TenantId = User.GetTenantId() }, req);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        await LoadRefs(payment);
        return Ok(ToDto(payment));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.PaymentsEdit)]
    public async Task<ActionResult<PaymentDto>> Update(Guid id, PaymentRequest req)
    {
        var payment = await FindAsync(id);
        if (payment is null) return NotFound();
        Map(payment, req);
        await db.SaveChangesAsync();
        await LoadRefs(payment);
        return Ok(ToDto(payment));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.PaymentsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var payment = await FindAsync(id);
        if (payment is null) return NotFound();
        db.Payments.Remove(payment);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Payment?> FindAsync(Guid id) =>
        await db.Payments.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == User.GetTenantId());

    private async Task LoadRefs(Payment payment)
    {
        await db.Entry(payment).Reference(p => p.Client).LoadAsync();
        await db.Entry(payment).Reference(p => p.Property).LoadAsync();
    }

    private static Payment Map(Payment p, PaymentRequest req)
    {
        p.ReceiptNo = req.ReceiptNo;
        p.ClientId = req.ClientId;
        p.PropertyId = req.PropertyId;
        p.Amount = req.Amount;
        p.PaymentDate = req.PaymentDate;
        p.Notes = req.Notes;
        return p;
    }

    private static PaymentDto ToDto(Payment p) => new(
        p.Id, p.ReceiptNo, p.ClientId, p.PropertyId, p.Amount, p.PaymentDate, p.Notes, p.CreatedAt,
        p.Client is null ? null : new ClientDto(p.Client.Id, p.Client.Name, p.Client.Cnic, p.Client.Phone, p.Client.Address, p.Client.FatherHusband, p.Client.Notes, p.Client.CreatedAt),
        p.Property is null ? null : new PropertyDto(p.Property.Id, p.Property.PropertyNumber, p.Property.PropertyType, p.Property.Marla, p.Property.TotalPrice, p.Property.BookingDate, p.Property.Status, p.Property.ClientId, p.Property.Notes, p.Property.CreatedAt, null));
}
