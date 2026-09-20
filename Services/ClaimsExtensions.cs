using System.Security.Claims;

namespace SocietyKhata.Api.Services;

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static int GetTenantId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue("tenant_id")!);

    public static string GetRole(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role)!;
}
