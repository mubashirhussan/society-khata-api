namespace SocietyKhata.Api.Models;

public class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? PaidTo { get; set; }
    public DateOnly ExpenseDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
}
