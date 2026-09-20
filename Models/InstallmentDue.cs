namespace SocietyKhata.Api.Models;

public class InstallmentDue
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public int PropertyId { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public string Status { get; set; } = "pending";
    public string? PlanFrequency { get; set; }
    public int? PaymentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Client? Client { get; set; }
    public Property? Property { get; set; }
    public Payment? Payment { get; set; }

    public decimal RemainingAmount => Math.Max(0, Amount - AmountPaid);
}
