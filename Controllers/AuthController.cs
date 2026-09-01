using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Services;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        try
        {
            return Ok(await auth.RegisterAsync(req));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        try
        {
            return Ok(await auth.LoginAsync(req));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var me = await auth.GetMeAsync(User.GetUserId());
        return me is null ? Unauthorized() : Ok(me);
    }
}
