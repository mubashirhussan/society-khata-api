namespace SocietyKhata.Api.Models;

public class Permission
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    public ICollection<TenantRolePermission> RolePermissions { get; set; } = new List<TenantRolePermission>();
}
