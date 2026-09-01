namespace SocietyKhata.Api.Models;

public class Property
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string PropertyNumber { get; set; } = string.Empty;
    public string PropertyType { get; set; } = "plot";
    public decimal? Marla { get; set; }
    public decimal TotalPrice { get; set; }
    public DateOnly? BookingDate { get; set; }
    public string Status { get; set; } = "available";
    public Guid? ClientId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public Client? Client { get; set; }
}
