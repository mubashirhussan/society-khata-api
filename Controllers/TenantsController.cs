using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyKhata.Api.Authorization;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Services;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TenantsController(TenantLogoStorage logos, AuthService auth) : ControllerBase
{
    [HttpGet("logo")]
    public IActionResult GetLogo()
    {
        var tenantId = User.GetTenantId();
        var path = logos.FindPath(tenantId);
        if (path is null) return NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(path, contentType);
    }

    [HttpPost("logo")]
    [RequirePermission(PermissionKeys.UsersManage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<UserDto>> UploadLogo(IFormFile? logo)
    {
        if (logo is null || logo.Length == 0 || logo.Length > 5 * 1024 * 1024)
            return BadRequest("Please choose an image smaller than 5 MB.");
        if (!logos.AllowedContentTypes.ContainsKey(logo.ContentType))
            return BadRequest("Only JPG, PNG, and WebP images are supported.");

        try
        {
            await logos.SaveAsync(User.GetTenantId(), logo);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var me = await auth.GetMeAsync(User.GetUserId());
        return me is null ? Unauthorized() : Ok(me);
    }
}
