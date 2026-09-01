namespace SocietyKhata.Api.Models;

public static class RoleNames
{
    public const string Admin = "Admin";
    public const string Accountant = "Accountant";
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid TenantRoleId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public TenantRole? TenantRole { get; set; }
}
