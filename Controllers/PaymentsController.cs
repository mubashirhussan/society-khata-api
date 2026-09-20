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
            .Where(p => p.TenantId == tenantId && (p.Client == null || !p.Client.IsDeleted))
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();
        var dues = await db.InstallmentDues
            .Include(d => d.Client)
            .Include(d => d.Property)
            .Where(d => d.TenantId == tenantId && d.Status == "pending"
                && d.Property != null && d.ClientId == d.Property.ClientId
                && (d.Client == null || !d.Client.IsDeleted))
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

    // One aggregated call for the payments list screen: a per-client summary row
    // (totals + pending) computed server-side instead of shipping every payment,
    // ledger row, client and property to the browser for it to group there.
    [HttpGet("summary")]
    [RequirePermission(PermissionKeys.PaymentsView)]
    public async Task<ActionResult<List<PaymentClientSummaryDto>>> Summary()
    {
        var tenantId = User.GetTenantId();
        var payments = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.Property)
            .Where(p => p.TenantId == tenantId && (p.Client == null || !p.Client.IsDeleted))
            .ToListAsync();
        var dues = await db.InstallmentDues
            .Include(d => d.Client)
            .Include(d => d.Property)
            .Where(d => d.TenantId == tenantId && d.Status == "pending"
                && d.Property != null && d.ClientId == d.Property.ClientId
                && (d.Client == null || !d.Client.IsDeleted))
            .ToListAsync();

        var groups = new Dictionary<int, ClientAggregate>();
        foreach (var payment in payments)
        {
            var clientId = payment.ClientId ?? payment.Property?.ClientId;
            var client = payment.Client;
            if (clientId is null || client is null) continue;

            if (!groups.TryGetValue(clientId.Value, out var agg))
            {
                agg = new ClientAggregate { Client = client, LastPaymentDate = payment.PaymentDate };
                groups[clientId.Value] = agg;
            }
            agg.TotalReceived += payment.Amount;
            if (payment.Property is not null)
            {
                agg.PropertyNumbers.Add(payment.Property.PropertyNumber);
                agg.PropertyIds.Add(payment.Property.Id);
                agg.PropertyPrices[payment.Property.Id] = payment.Property.TotalPrice;
            }
            if (payment.PaymentDate > agg.LastPaymentDate) agg.LastPaymentDate = payment.PaymentDate;
        }

        // A client can still owe a scheduled installment after every payment toward it has
        // been deleted (the plan itself isn't removed). Without this, that client would
        // disappear from this list even though their own detail page still shows the plan.
        foreach (var due in dues)
        {
            if (due.Client is null) continue;
            if (!groups.TryGetValue(due.ClientId, out var agg))
            {
                agg = new ClientAggregate { Client = due.Client, LastPaymentDate = due.DueDate };
                groups[due.ClientId] = agg;
            }
            if (due.Property is not null)
            {
                agg.PropertyNumbers.Add(due.Property.PropertyNumber);
                agg.PropertyIds.Add(due.Property.Id);
                agg.PropertyPrices[due.Property.Id] = due.Property.TotalPrice;
            }
        }

        var result = new List<PaymentClientSummaryDto>();
        foreach (var (clientId, agg) in groups)
        {
            var scheduledPropertyIds = dues
                .Where(d => d.ClientId == clientId)
                .Select(d => d.PropertyId)
                .ToHashSet();
            var scheduledPending = dues.Where(d => d.ClientId == clientId).Sum(d => d.RemainingAmount);

            var paidByProperty = payments
                .Where(p => p.PropertyId.HasValue && agg.PropertyIds.Contains(p.PropertyId.Value))
                .GroupBy(p => p.PropertyId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

            var unscheduledPending = agg.PropertyIds
                .Where(id => !scheduledPropertyIds.Contains(id))
                .Sum(id => Math.Max(0, agg.PropertyPrices.GetValueOrDefault(id) - paidByProperty.GetValueOrDefault(id)));

            var totalPlotAmount = agg.PropertyIds.Sum(id => agg.PropertyPrices.GetValueOrDefault(id));

            result.Add(new PaymentClientSummaryDto(
                clientId, ToClientDto(agg.Client)!, agg.PropertyNumbers.ToList(),
                totalPlotAmount, agg.TotalReceived, scheduledPending + unscheduledPending, agg.LastPaymentDate));
        }

        return Ok(result.OrderByDescending(r => r.LastPaymentDate).ToList());
    }

    private sealed class ClientAggregate
    {
        public required Client Client { get; init; }
        public DateOnly LastPaymentDate { get; set; }
        public decimal TotalReceived { get; set; }
        public HashSet<string> PropertyNumbers { get; } = [];
        public HashSet<int> PropertyIds { get; } = [];
        public Dictionary<int, decimal> PropertyPrices { get; } = [];
    }

    // Detail screen for one client, scoped in the query itself instead of pulling
    // every tenant payment/ledger/client row to the browser and filtering there.
    [HttpGet("client/{clientId:int}")]
    [RequirePermission(PermissionKeys.PaymentsView)]
    public async Task<ActionResult<PaymentClientDetailDto>> ClientDetail(int clientId)
    {
        var tenantId = User.GetTenantId();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.TenantId == tenantId && !c.IsDeleted);
        if (client is null) return NotFound();

        var directPayments = await db.Payments
            .Include(p => p.Client)
            .Include(p => p.Property)
            .Where(p => p.TenantId == tenantId && p.ClientId == clientId)
            .ToListAsync();

        var propertyIds = directPayments
            .Where(p => p.PropertyId.HasValue)
            .Select(p => p.PropertyId!.Value)
            .Distinct()
            .ToList();

        // legacy payments recorded on the same property without a client id
        var relatedPayments = propertyIds.Count == 0
            ? []
            : await db.Payments
                .Include(p => p.Client)
                .Include(p => p.Property)
                .Where(p => p.TenantId == tenantId && p.ClientId == null
                    && p.PropertyId.HasValue && propertyIds.Contains(p.PropertyId.Value))
                .ToListAsync();

        var payments = directPayments.Concat(relatedPayments)
            .OrderByDescending(p => p.PaymentDate)
            .ToList();

        var scheduledInstallments = await db.InstallmentDues
            .Include(d => d.Property)
            .Where(d => d.TenantId == tenantId && d.Status == "pending" && d.ClientId == clientId)
            .OrderBy(d => d.DueDate)
            .ToListAsync();
        var scheduledPropertyIds = scheduledInstallments.Select(d => d.PropertyId).ToHashSet();

        var clientPropertyIds = payments
            .Where(p => p.PropertyId.HasValue)
            .Select(p => p.PropertyId!.Value)
            .Distinct();

        var unscheduledBalances = new List<PendingInstallmentDto>();
        foreach (var propertyId in clientPropertyIds)
        {
            if (scheduledPropertyIds.Contains(propertyId)) continue;
            var property = payments.First(p => p.PropertyId == propertyId).Property;
            var paid = payments.Where(p => p.PropertyId == propertyId).Sum(p => p.Amount);
            var remaining = Math.Max(0, (property?.TotalPrice ?? paid) - paid);
            if (remaining <= 0) continue;
            unscheduledBalances.Add(new PendingInstallmentDto(
                -propertyId, null, propertyId, ToPropertyDto(property), remaining, "pending"));
        }

        var pendingInstallments = scheduledInstallments
            .Select(d => new PendingInstallmentDto(
                d.Id, d.DueDate, d.PropertyId, ToPropertyDto(d.Property),
                d.RemainingAmount, d.DueDate < today ? "overdue" : "pending"))
            .Concat(unscheduledBalances)
            .ToList();

        var propertyTotals = new Dictionary<int, decimal>();
        foreach (var p in payments)
            if (p.Property is not null) propertyTotals[p.Property.Id] = p.Property.TotalPrice;
        foreach (var pi in pendingInstallments)
            if (pi.Property is not null) propertyTotals[pi.Property.Id] = pi.Property.TotalPrice;

        var planFrequencies = scheduledInstallments
            .Select(d => d.PlanFrequency)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!)
            .Distinct()
            .ToList();

        return Ok(new PaymentClientDetailDto(
            ToClientDto(client)!,
            payments.Select(p => ToDto(p)).ToList(),
            pendingInstallments,
            propertyTotals.Values.Sum(),
            payments.Sum(p => p.Amount),
            pendingInstallments.Sum(p => p.Amount),
            planFrequencies));
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
            ApplyPaymentToSchedule(payment, overflowChain, validation.Amount, tenantId);
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

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionKeys.PaymentsEdit)]
    public async Task<ActionResult<PaymentDto>> Update(int id, PaymentRequest req)
    {
        var payment = await FindAsync(id);
        if (payment is null) return NotFound();
        if (req.InstallmentSchedule is { Count: > 0 })
            return BadRequest(new { error = "An installment plan cannot be replaced while editing a receipt." });

        var linkedDue = await FindLinkedDueAsync(payment);
        if (linkedDue is not null && (req.ClientId != linkedDue.ClientId || req.PropertyId != linkedDue.PropertyId))
            return BadRequest(new { error = "A scheduled installment's client and property cannot be changed." });

        var oldPropertyId = payment.PropertyId;
        var oldAmount = payment.Amount;
        var validation = await ValidateAndCalculateAmount(req, payment.TenantId, payment.Id);
        if (validation.Error is not null) return BadRequest(new { error = validation.Error });

        await using var transaction = await db.Database.BeginTransactionAsync();

        // A payment tied to the schedule may have overflowed across several installments
        // when it was made. To edit it correctly we undo exactly what it contributed (via
        // its recorded allocations, not a guess), then re-apply the new amount fresh across
        // the same chain — rather than only touching the one due it was originally linked to.
        var chain = new List<InstallmentDue>();
        if (linkedDue is not null)
        {
            await ReversePaymentFromScheduleAsync(payment, linkedDue);
            await db.SaveChangesAsync();

            chain.Add(linkedDue);
            chain.AddRange(await db.InstallmentDues
                .Where(d => d.TenantId == payment.TenantId && d.PropertyId == linkedDue.PropertyId
                    && d.ClientId == linkedDue.ClientId && d.Status == "pending" && d.Id != linkedDue.Id)
                .OrderBy(d => d.DueDate)
                .ToListAsync());

            var capacity = chain.Sum(d => d.RemainingAmount);
            if (validation.Amount > capacity)
                return BadRequest(new { error = $"Payment cannot exceed the total remaining balance of {capacity:N0} across pending installments." });
        }

        // Only a standalone (not tied to one installment) payment can absorb extra amount
        // into other pending installments, since that just adjusts their AmountPaid directly.
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
            ApplyPaymentToSchedule(payment, chain, validation.Amount, payment.TenantId);
        else if (extraToApply > 0)
            ApplyAmountToDueChain(payment, overflowDues, extraToApply);

        Map(payment, req, validation.Amount);
        await db.SaveChangesAsync();
        await SyncProperty(oldPropertyId);
        if (payment.PropertyId != oldPropertyId) await SyncProperty(payment.PropertyId);
        await transaction.CommitAsync();
        await LoadRefs(payment);
        return Ok(ToDto(payment));
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionKeys.PaymentsDelete)]
    public async Task<IActionResult> Delete(int id)
    {
        var payment = await FindAsync(id);
        if (payment is null) return NotFound();
        var propertyId = payment.PropertyId;
        var linkedDue = await FindLinkedDueAsync(payment);
        if (linkedDue is not null)
            await ReversePaymentFromScheduleAsync(payment, linkedDue);
        else
            await ReopenScheduleForDeletedAdvanceAsync(payment);
        payment.IsDeleted = true;
        await db.SaveChangesAsync();
        await SyncProperty(propertyId);
        return NoContent();
    }

    private async Task<Payment?> FindAsync(int id) =>
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

    // Applies one receipt's amount across a chain of pending dues (starting from the due the
    // receipt targets), recording exactly how much landed on each one. A single receipt stays
    // a single Payment row even when it overflows into later installments — only the ledger
    // rows underneath track the split, so the receipt list never fragments into duplicates.
    private void ApplyPaymentToSchedule(Payment payment, List<InstallmentDue> dueChain, decimal totalAmount, int tenantId)
    {
        payment.InstallmentDueId = dueChain[0].Id;
        var remaining = totalAmount;
        foreach (var due in dueChain)
        {
            if (remaining <= 0) break;
            var portion = Math.Min(remaining, due.RemainingAmount);
            if (portion <= 0) continue;
            due.AmountPaid += portion;
            SyncDueStatus(due, payment);
            db.PaymentInstallmentAllocations.Add(new PaymentInstallmentAllocation
            {
                TenantId = tenantId,
                Payment = payment,
                InstallmentDueId = due.Id,
                Amount = portion,
            });
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
            SyncDueStatus(due, payment);
            remaining -= portion;
        }
    }

    // Undoes exactly what this payment contributed to the schedule using its recorded
    // allocations (correct even if it overflowed across several dues), falling back to the
    // old single-due math only for payments created before allocations were tracked.
    private async Task ReversePaymentFromScheduleAsync(Payment payment, InstallmentDue fallbackDue)
    {
        var allocations = await db.PaymentInstallmentAllocations
            .Include(a => a.InstallmentDue)
            .Where(a => a.PaymentId == payment.Id)
            .ToListAsync();

        if (allocations.Count == 0)
        {
            fallbackDue.AmountPaid = Math.Max(0, fallbackDue.AmountPaid - payment.Amount);
            if (fallbackDue.PaymentId == payment.Id) fallbackDue.PaymentId = null;
            SyncDueStatus(fallbackDue, completingPayment: null);
            return;
        }

        foreach (var allocation in allocations)
        {
            var due = allocation.InstallmentDue;
            if (due is null) continue;
            due.AmountPaid = Math.Max(0, due.AmountPaid - allocation.Amount);
            if (due.PaymentId == payment.Id) due.PaymentId = null;
            SyncDueStatus(due, completingPayment: null);
        }
        db.PaymentInstallmentAllocations.RemoveRange(allocations);
    }

    // The payment that originally founded an installment schedule (its "advance") never
    // touches any due row directly — its amount is simply netted out of the schedule's total
    // at creation time (schedule sum = remaining - advance). Deleting that payment later must
    // add an equivalent new pending installment back, otherwise the schedule keeps assuming
    // money was collected that no longer was.
    private async Task ReopenScheduleForDeletedAdvanceAsync(Payment payment)
    {
        if (payment.PropertyId is null || payment.ClientId is null || payment.Amount <= 0) return;

        var hasSchedule = await db.InstallmentDues.AnyAsync(d =>
            d.TenantId == payment.TenantId && d.PropertyId == payment.PropertyId
            && d.ClientId == payment.ClientId && d.Status != "cancelled");
        if (!hasSchedule) return;

        db.InstallmentDues.Add(new InstallmentDue
        {
            TenantId = payment.TenantId,
            ClientId = payment.ClientId.Value,
            PropertyId = payment.PropertyId.Value,
            DueDate = payment.PaymentDate,
            Amount = payment.Amount,
        });
    }

    private static void SyncDueStatus(InstallmentDue due, Payment? completingPayment)
    {
        if (due.AmountPaid >= due.Amount)
        {
            due.AmountPaid = due.Amount;
            due.Status = "paid";
            if (completingPayment is not null)
                due.Payment = completingPayment;
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
        int tenantId,
        int? excludedPaymentId = null)
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
        PaymentRequest req, int tenantId, decimal remaining, decimal paymentAmount)
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

    private async Task SyncProperty(int? propertyId)
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

        // A payment can be deleted (e.g. entered by mistake) without cancelling the
        // installment plan built on top of it. Only clear the property's owner once
        // neither a payment nor an active plan claims it — otherwise the property looks
        // "available" while its old client still owes a pending schedule, which lets a
        // second plan get created on the same property and doubles up the pending total.
        var activeDue = await db.InstallmentDues
            .Where(d => d.TenantId == tenantId && d.PropertyId == propertyId && d.Status != "cancelled")
            .OrderBy(d => d.DueDate)
            .FirstOrDefaultAsync();

        if (totalPaid <= 0 && activeDue is null)
        {
            property.Status = "available";
            property.ClientId = null;
            property.BookingDate = null;
        }
        else
        {
            property.Status = totalPaid >= property.TotalPrice ? "sold" : "booked";
            property.ClientId = payments.Count > 0 ? payments[0].ClientId : activeDue!.ClientId;
            property.BookingDate = payments.Count > 0 ? payments[0].PaymentDate : activeDue!.DueDate;
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
