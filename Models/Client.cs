namespace SocietyKhata.Api.Models;

public class Client
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Cnic { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? FatherHusband { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public ICollection<Property> Properties { get; set; } = new List<Property>();
}
