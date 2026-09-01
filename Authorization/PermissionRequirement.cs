using Microsoft.AspNetCore.Authorization;

namespace SocietyKhata.Api.Authorization;

public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim("permission", requirement.Permission))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePermissionAttribute(string permission) : AuthorizeAttribute($"perm:{permission}");
