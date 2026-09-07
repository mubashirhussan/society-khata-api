namespace SocietyKhata.Api.Models;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string? ReceiptNo { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? PropertyId { get; set; }
    public Guid? InstallmentDueId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public Client? Client { get; set; }
    public Property? Property { get; set; }
    public InstallmentDue? InstallmentDue { get; set; }
}
