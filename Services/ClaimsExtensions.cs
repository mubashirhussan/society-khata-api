using System.Security.Claims;

namespace SocietyKhata.Api.Services;

public static class ClaimsExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid GetTenantId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("tenant_id")!);

    public static string GetRole(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role)!;
}
