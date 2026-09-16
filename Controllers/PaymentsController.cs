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
        return Ok(items.Select(p => ToDto(p)).ToList());
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
            .Where(d => d.TenantId == tenantId && d.Status == "pending"
                && d.Property != null && d.ClientId == d.Property.ClientId)
            .OrderBy(d => d.DueDate)
            .ToListAsync();

        var rows = dues.Select(d => new PaymentLedgerDto(
                d.Id, "installment", d.DueDate < today ? "overdue" : "pending", null,
                d.ClientId, d.PropertyId, d.RemainingAmount, d.DueDate, null, null, d.PlanFrequency,
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
            if (linkedDue is null || linkedDue.Status != "pending")
                return BadRequest(new { error = "The selected installment is no longer pending." });
            if (req.ClientId != linkedDue.ClientId || req.PropertyId != linkedDue.PropertyId)
                return BadRequest(new { error = "The payment must use the installment's client and property." });
            if (req.Amount <= 0)
                return BadRequest(new { error = "Payment amount must be greater than zero." });
        }

        var validation = await ValidateAndCalculateAmount(req, tenantId);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });

        var hasPlan = req.PropertyId.HasValue && await db.InstallmentDues.AnyAsync(d =>
            d.TenantId == tenantId && d.PropertyId == req.PropertyId && d.Status != "cancelled"
            && d.Property != null && d.ClientId == d.Property.ClientId);
        if (hasPlan && linkedDue is null)
            return BadRequest(new { error = "This property already has an installment plan. Receive one of its pending installments instead." });

        var scheduleError = await ValidateSchedule(
            req, tenantId, validation.Remaining, validation.Amount);
        if (scheduleError is not null) return BadRequest(new { error = scheduleError });

        var overflowChain = new List<InstallmentDue>();
        if (linkedDue is not null)
        {
            overflowChain.Add(linkedDue);
            if (validation.Amount > linkedDue.RemainingAmount)
            {
                overflowChain.AddRange(await db.InstallmentDues
                    .Where(d => d.TenantId == tenantId && d.PropertyId == linkedDue.PropertyId
                        && d.ClientId == linkedDue.ClientId && d.Status == "pending" && d.Id != linkedDue.Id)
                    .OrderBy(d => d.DueDate)
                    .ToListAsync());

                var capacity = overflowChain.Sum(d => d.RemainingAmount);
                if (validation.Amount > capacity)
                    return BadRequest(new { error = $"Payment cannot exceed the total remaining balance of {capacity:N0} across pending installments." });
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var payment = Map(new Payment { TenantId = tenantId }, req, validation.Amount);
        db.Payments.Add(payment);
        if (linkedDue is not null)
            ApplyPaymentWithOverflow(payment, overflowChain, validation.Amount, tenantId);
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

        var linkedDue = await FindLinkedDueAsync(payment);
        if (linkedDue is not null)
        {
            if (req.ClientId != linkedDue.ClientId || req.PropertyId != linkedDue.PropertyId)
                return BadRequest(new { error = "A scheduled installment's client and property cannot be changed." });
            var maxAllowed = linkedDue.RemainingAmount + payment.Amount;
            if (req.Amount <= 0)
                return BadRequest(new { error = "Payment amount must be greater than zero." });
            if (req.Amount > maxAllowed)
                return BadRequest(new { error = $"Payment cannot exceed the remaining installment of {maxAllowed:N0}." });
        }

        var oldPropertyId = payment.PropertyId;
        var oldAmount = payment.Amount;
        var validation = await ValidateAndCalculateAmount(req, payment.TenantId, payment.Id);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });

        // A payment linked to one specific installment stays capped to that installment (see
        // maxAllowed above) — spreading an edit across several installments would mean creating
        // new Payment rows every time the receipt is saved, duplicating money on every re-edit.
        // Only a standalone (not tied to one installment) payment can absorb extra amount into
        // other pending installments, since that just adjusts their AmountPaid directly with no
        // new rows involved.
        var overflowDues = new List<InstallmentDue>();
        var extraToApply = 0m;
        if (linkedDue is null && req.PropertyId.HasValue && validation.Amount > oldAmount)
        {
            extraToApply = validation.Amount - oldAmount;
            overflowDues = await db.InstallmentDues
                .Where(d => d.TenantId == payment.TenantId && d.PropertyId == req.PropertyId
                    && d.ClientId == req.ClientId && d.Status == "pending")
                .OrderBy(d => d.DueDate)
                .ToListAsync();

            var capacity = overflowDues.Sum(d => d.RemainingAmount);
            if (extraToApply > capacity)
                return BadRequest(new { error = $"Payment cannot exceed the total remaining balance of {capacity:N0} across pending installments." });
        }

        if (linkedDue is not null)
            AdjustDueForPaymentEdit(linkedDue, payment, validation.Amount);
        else if (extraToApply > 0)
            ApplyAmountToDueChain(payment, overflowDues, extraToApply);

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
        var linkedDue = await FindLinkedDueAsync(payment);
        if (linkedDue is not null)
            RemovePaymentFromDue(linkedDue, payment);
        payment.IsDeleted = true;
        await db.SaveChangesAsync();
        await SyncProperty(propertyId);
        return NoContent();
    }

    private async Task<Payment?> FindAsync(Guid id) =>
        await db.Payments.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == User.GetTenantId());

    private async Task<InstallmentDue?> FindLinkedDueAsync(Payment payment)
    {
        if (payment.InstallmentDueId.HasValue)
        {
            return await db.InstallmentDues.FirstOrDefaultAsync(d =>
                d.Id == payment.InstallmentDueId && d.TenantId == payment.TenantId);
        }

        return await db.InstallmentDues.FirstOrDefaultAsync(d =>
            d.TenantId == payment.TenantId && d.PaymentId == payment.Id);
    }

    private static void ApplyPaymentToDue(InstallmentDue due, Payment payment, decimal amount)
    {
        payment.InstallmentDueId = due.Id;
        due.AmountPaid += amount;
        SyncDueStatus(due, payment.Id);
    }

    private void ApplyPaymentWithOverflow(Payment payment, List<InstallmentDue> dueChain, decimal totalAmount, Guid tenantId)
    {
        var remaining = totalAmount;
        for (var i = 0; i < dueChain.Count && remaining > 0; i++)
        {
            var due = dueChain[i];
            var portion = Math.Min(remaining, due.RemainingAmount);

            if (i == 0)
            {
                payment.Amount = portion;
                ApplyPaymentToDue(due, payment, portion);
            }
            else
            {
                var overflowPayment = new Payment
                {
                    TenantId = tenantId,
                    ReceiptNo = payment.ReceiptNo,
                    ClientId = payment.ClientId,
                    PropertyId = payment.PropertyId,
                    Amount = portion,
                    PaymentDate = payment.PaymentDate,
                    Notes = payment.Notes,
                    SourcePaymentId = payment.Id,
                };
                db.Payments.Add(overflowPayment);
                ApplyPaymentToDue(due, overflowPayment, portion);
            }

            remaining -= portion;
        }
    }

    private static void ApplyAmountToDueChain(Payment payment, List<InstallmentDue> pendingDues, decimal amountToApply)
    {
        var remaining = amountToApply;
        foreach (var due in pendingDues)
        {
            if (remaining <= 0) break;
            var portion = Math.Min(remaining, due.RemainingAmount);
            due.AmountPaid += portion;
            SyncDueStatus(due, payment.Id);
            remaining -= portion;
        }
    }

    private static void AdjustDueForPaymentEdit(InstallmentDue due, Payment payment, decimal newAmount)
    {
        due.AmountPaid = Math.Max(0, due.AmountPaid - payment.Amount + newAmount);
        payment.InstallmentDueId = due.Id;
        SyncDueStatus(due, payment.Id);
    }

    private static void RemovePaymentFromDue(InstallmentDue due, Payment payment)
    {
        due.AmountPaid = Math.Max(0, due.AmountPaid - payment.Amount);
        if (due.PaymentId == payment.Id)
            due.PaymentId = null;
        SyncDueStatus(due, completingPaymentId: null);
    }

    private static void SyncDueStatus(InstallmentDue due, Guid? completingPaymentId)
    {
        if (due.AmountPaid >= due.Amount)
        {
            due.AmountPaid = due.Amount;
            due.Status = "paid";
            if (completingPaymentId.HasValue)
                due.PaymentId = completingPaymentId;
        }
        else
        {
            due.Status = "pending";
            due.PaymentId = null;
        }
    }

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

        var clientExists = await db.Clients.AnyAsync(c => c.Id == req.ClientId && c.TenantId == tenantId && !c.IsDeleted);
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
            d.TenantId == tenantId && d.PropertyId == req.PropertyId && d.Status != "cancelled"
            && d.Property != null && d.ClientId == d.Property.ClientId))
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
