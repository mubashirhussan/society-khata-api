namespace SocietyKhata.Api.Models;

public class Payment
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string? ReceiptNo { get; set; }
    public int? ClientId { get; set; }
    public int? PropertyId { get; set; }
    public int? InstallmentDueId { get; set; }
    public int? SourcePaymentId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public Client? Client { get; set; }
    public Property? Property { get; set; }
    public InstallmentDue? InstallmentDue { get; set; }
}
