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

    [HttpGet("ledger")]
    [RequirePermission(PermissionKeys.PaymentsView)]
    public async Task<ActionResult<List<PaymentLedgerDto>>> Ledger()
    {
        var tenantId = User.GetTenantId();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var payments = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.Property)
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();
        var dues = await db.InstallmentDues
            .Include(d => d.Client)
            .Include(d => d.Property)
            .Where(d => d.TenantId == tenantId && d.Status == "pending")
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        var rows = dues.Select(d => new PaymentLedgerDto(
                d.Id, "installment", d.DueDate < today ? "overdue" : "pending", null,
                d.ClientId, d.PropertyId, d.Amount, d.DueDate, null, null, d.PlanFrequency,
                ToClientDto(d.Client), ToPropertyDto(d.Property)))
            .ToList();
        rows.AddRange(payments.Select(p => new PaymentLedgerDto(
            p.Id, "payment", "received", p.ReceiptNo, p.ClientId, p.PropertyId,
            p.Amount, p.PaymentDate, p.Notes, p.Id, null,
            ToClientDto(p.Client), ToPropertyDto(p.Property))));
        return Ok(rows);
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.PaymentsCreate)]
    public async Task<ActionResult<PaymentDto>> Create(PaymentRequest req)
    {
        var tenantId = User.GetTenantId();
        InstallmentDue? linkedDue = null;
        if (req.InstallmentDueId.HasValue)
        {
            linkedDue = await db.InstallmentDues.FirstOrDefaultAsync(d =>
                d.Id == req.InstallmentDueId && d.TenantId == tenantId);
            if (linkedDue is null || linkedDue.Status != "pending" || linkedDue.PaymentId is not null)
                return BadRequest(new { error = "The selected installment is no longer pending." });
            if (req.ClientId != linkedDue.ClientId || req.PropertyId != linkedDue.PropertyId)
                return BadRequest(new { error = "The payment must use the installment's client and property." });
            if (req.Amount != linkedDue.Amount)
                return BadRequest(new { error = "The payment amount must match the scheduled installment." });
        }

        var validation = await ValidateAndCalculateAmount(req, tenantId);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });

        var hasPlan = req.PropertyId.HasValue && await db.InstallmentDues.AnyAsync(d =>
            d.TenantId == tenantId && d.PropertyId == req.PropertyId);
        if (hasPlan && linkedDue is null)
            return BadRequest(new { error = "This property already has an installment plan. Receive one of its pending installments instead." });

        var scheduleError = await ValidateSchedule(
            req, tenantId, validation.Remaining, validation.Amount);
        if (scheduleError is not null) return BadRequest(new { error = scheduleError });

        await using var transaction = await db.Database.BeginTransactionAsync();
        var payment = Map(new Payment { TenantId = tenantId }, req, validation.Amount);
        db.Payments.Add(payment);
        if (linkedDue is not null)
        {
            linkedDue.Status = "paid";
            linkedDue.PaymentId = payment.Id;
        }
        else if (req.InstallmentSchedule is { Count: > 0 })
        {
            db.InstallmentDues.AddRange(req.InstallmentSchedule.Select(item => new InstallmentDue
            {
                TenantId = tenantId,
                ClientId = req.ClientId!.Value,
                PropertyId = req.PropertyId!.Value,
                DueDate = item.DueDate,
                Amount = item.Amount,
                PlanFrequency = req.PlanFrequency,
            }));
        }
        await db.SaveChangesAsync();
        await SyncProperty(payment.PropertyId);
        await transaction.CommitAsync();
        await LoadRefs(payment);
        return Ok(ToDto(payment));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.PaymentsEdit)]
    public async Task<ActionResult<PaymentDto>> Update(Guid id, PaymentRequest req)
    {
        var payment = await FindAsync(id);
        if (payment is null) return NotFound();
        if (req.InstallmentSchedule is { Count: > 0 })
            return BadRequest(new { error = "An installment plan cannot be replaced while editing a receipt." });

        var linkedDue = await db.InstallmentDues.FirstOrDefaultAsync(d =>
            d.TenantId == payment.TenantId && d.PaymentId == payment.Id);
        var propertyHasPlan = payment.PropertyId.HasValue && await db.InstallmentDues.AnyAsync(d =>
            d.TenantId == payment.TenantId && d.PropertyId == payment.PropertyId);
        if (linkedDue is not null)
        {
            if (req.ClientId != linkedDue.ClientId || req.PropertyId != linkedDue.PropertyId
                || req.Amount != linkedDue.Amount)
                return BadRequest(new { error = "A scheduled installment's client, property, and amount cannot be changed." });
        }
        else if (propertyHasPlan && (req.ClientId != payment.ClientId
            || req.PropertyId != payment.PropertyId || req.Amount != payment.Amount))
        {
            return BadRequest(new { error = "This receipt belongs to an installment plan. Only its receipt number, date, and notes can be changed." });
        }

        var oldPropertyId = payment.PropertyId;
        var validation = await ValidateAndCalculateAmount(req, payment.TenantId, payment.Id);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });

        Map(payment, req, validation.Amount);
        await db.SaveChangesAsync();
        await SyncProperty(oldPropertyId);
        if (payment.PropertyId != oldPropertyId) await SyncProperty(payment.PropertyId);
        await LoadRefs(payment);
        return Ok(ToDto(payment));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.PaymentsDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var payment = await FindAsync(id);
        if (payment is null) return NotFound();
        var propertyId = payment.PropertyId;
        var linkedDue = await db.InstallmentDues.FirstOrDefaultAsync(d =>
            d.TenantId == payment.TenantId && d.PaymentId == payment.Id);
        var propertyHasPlan = propertyId.HasValue && await db.InstallmentDues.AnyAsync(d =>
            d.TenantId == payment.TenantId && d.PropertyId == propertyId);
        if (propertyHasPlan && linkedDue is null)
            return BadRequest(new { error = "The initial payment cannot be deleted while its installment plan exists." });
        if (linkedDue is not null)
        {
            linkedDue.Status = "pending";
            linkedDue.PaymentId = null;
        }
        db.Payments.Remove(payment);
        await db.SaveChangesAsync();
        await SyncProperty(propertyId);
        return NoContent();
    }

    private async Task<Payment?> FindAsync(Guid id) =>
        await db.Payments.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == User.GetTenantId());

    private async Task LoadRefs(Payment payment)
    {
        await db.Entry(payment).Reference(p => p.Client).LoadAsync();
        await db.Entry(payment).Reference(p => p.Property).LoadAsync();
    }

    private async Task<(decimal Amount, decimal Remaining, string? Error)> ValidateAndCalculateAmount(
        PaymentRequest req,
        Guid tenantId,
        Guid? excludedPaymentId = null)
    {
        if (req.ClientId is null || req.PropertyId is null)
            return (0, 0, "Client and property are required.");

        var clientExists = await db.Clients.AnyAsync(c => c.Id == req.ClientId && c.TenantId == tenantId);
        if (!clientExists) return (0, 0, "The selected client was not found.");

        var property = await db.Properties.FirstOrDefaultAsync(
            p => p.Id == req.PropertyId && p.TenantId == tenantId);
        if (property is null) return (0, 0, "The selected property was not found.");
        if (property.ClientId is not null && property.ClientId != req.ClientId)
            return (0, 0, "This property is already booked by another client.");

        var alreadyPaid = await db.Payments
            .Where(p => p.TenantId == tenantId
                && p.PropertyId == req.PropertyId
                && (!excludedPaymentId.HasValue || p.Id != excludedPaymentId.Value))
            .SumAsync(p => (decimal?)p.Amount) ?? 0;
        var remaining = property.TotalPrice - alreadyPaid;
        if (remaining <= 0) return (0, remaining, "This property is already fully paid.");

        var amount = string.Equals(req.PaymentMethod, "full", StringComparison.OrdinalIgnoreCase)
            ? remaining
            : req.Amount;
        if (amount <= 0) return (0, remaining, "Payment amount must be greater than zero.");
        if (amount > remaining) return (0, remaining, $"Payment cannot exceed the remaining balance of {remaining:N0}.");

        return (amount, remaining, null);
    }

    private async Task<string?> ValidateSchedule(
        PaymentRequest req, Guid tenantId, decimal remaining, decimal paymentAmount)
    {
        if (req.InstallmentSchedule is not { Count: > 0 }) return null;
        if (!string.Equals(req.PaymentMethod, "installment", StringComparison.OrdinalIgnoreCase))
            return "A schedule can only be created for an installment payment.";
        if (req.InstallmentDueId.HasValue)
            return "A new schedule cannot be created while receiving an existing installment.";
        if (req.InstallmentSchedule.Any(item => item.Amount <= 0))
            return "Every installment amount must be greater than zero.";
        if (req.InstallmentSchedule.Any(item => item.DueDate < req.PaymentDate))
            return "Installment due dates cannot be before the payment date.";
        var frequencies = new[] { "monthly", "quarterly", "half-yearly", "yearly" };
        if (string.IsNullOrWhiteSpace(req.PlanFrequency) || !frequencies.Contains(req.PlanFrequency))
            return "Select a valid payment plan frequency.";
        if (req.InstallmentSchedule.Sum(item => item.Amount) != remaining - paymentAmount)
            return $"The installment schedule must total {(remaining - paymentAmount):N0}.";
        if (req.PropertyId.HasValue && await db.InstallmentDues.AnyAsync(d =>
            d.TenantId == tenantId && d.PropertyId == req.PropertyId))
            return "This property already has an installment plan.";
        return null;
    }

    private async Task SyncProperty(Guid? propertyId)
    {
        if (propertyId is null) return;

        var tenantId = User.GetTenantId();
        var property = await db.Properties.FirstOrDefaultAsync(
            p => p.Id == propertyId && p.TenantId == tenantId);
        if (property is null) return;

        var payments = await db.Payments
            .Where(p => p.TenantId == tenantId && p.PropertyId == propertyId)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync();
        var totalPaid = payments.Sum(p => p.Amount);

        if (totalPaid <= 0)
        {
            property.Status = "available";
            property.ClientId = null;
            property.BookingDate = null;
        }
        else
        {
            property.Status = totalPaid >= property.TotalPrice ? "sold" : "booked";
            property.ClientId = payments[0].ClientId;
            property.BookingDate = payments[0].PaymentDate;
        }

        await db.SaveChangesAsync();
    }

    private static Payment Map(Payment p, PaymentRequest req, decimal amount)
    {
        p.ReceiptNo = req.ReceiptNo;
        p.ClientId = req.ClientId;
        p.PropertyId = req.PropertyId;
        p.Amount = amount;
        p.PaymentDate = req.PaymentDate;
        p.Notes = req.Notes;
        return p;
    }

    private static PaymentDto ToDto(Payment p) => new(
        p.Id, p.ReceiptNo, p.ClientId, p.PropertyId, p.Amount, p.PaymentDate, p.Notes, p.CreatedAt,
        ToClientDto(p.Client), ToPropertyDto(p.Property));

    private static ClientDto? ToClientDto(Client? client) => client is null ? null : new ClientDto(
        client.Id, client.Name, client.Cnic, client.Phone, client.Address,
        client.FatherHusband, client.Notes, client.CreatedAt);

    private static PropertyDto? ToPropertyDto(Property? property) => property is null ? null : new PropertyDto(
        property.Id, property.PropertyNumber, property.PropertyType, property.Marla,
        property.TotalPrice, property.BookingDate, property.Status, property.ClientId,
        property.Notes, property.CreatedAt, null);
}
