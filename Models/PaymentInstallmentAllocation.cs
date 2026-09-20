namespace SocietyKhata.Api.Models;

// Records exactly how much of a single receipt was applied to a single installment due.
// A receipt that overflows across several dues gets one row per due it touched, so an
// edit or delete can reverse precisely what that receipt contributed — instead of guessing
// from the due's current (possibly further-modified) AmountPaid.
public class PaymentInstallmentAllocation
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int PaymentId { get; set; }
    public int InstallmentDueId { get; set; }
    public decimal Amount { get; set; }

    public Payment? Payment { get; set; }
    public InstallmentDue? InstallmentDue { get; set; }
}
