namespace SocietyKhata.Api.Models;

public class TenantRole
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<TenantRolePermission> RolePermissions { get; set; } = new List<TenantRolePermission>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class TenantRolePermission
{
    public Guid TenantRoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;

    public TenantRole? TenantRole { get; set; }
    public Permission? Permission { get; set; }
}
