using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Security.Dtos;
using SiagroB1.Security.Services;

namespace SiagroB1.Gateway.Controllers;

[ApiController]
[Route("security/users")]
public class UserController(UserService service, UserProfileService profileService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("createAdminUser")]
    public async Task<IActionResult> CreateAdminUser()
    {
        try
        {
            return Ok(await service.CreateAdminUserAsync());
        }
        catch (Exception e)
        {
            return e is ApplicationException ? BadRequest(e.Message) : StatusCode(500, e.Message);
        }
    }

    /// <summary>
    /// Perfil do usuário da sessão.
    ///
    /// Todos os endpoints <c>me/*</c> resolvem o usuário pelas claims, nunca por um identificador
    /// vindo da requisição: é o que impede um usuário de mexer no perfil de outro.
    /// </summary>
    [Authorize]
    [HttpGet("me/profile")]
    public async Task<IActionResult> GetMyProfile()
    {
        var profile = await profileService.GetProfileAsync(CurrentUsername);

        return profile is null ? NotFound(new { message = "Usuário não encontrado." }) : Ok(profile);
    }

    [Authorize]
    [HttpGet("me/photo")]
    public async Task<IActionResult> GetMyPhoto()
    {
        var photo = await profileService.GetPhotoAsync(CurrentUsername);

        // 204 em vez de 404: a ausência de foto é o estado normal de quem nunca subiu uma, e um
        // 404 apareceria como erro no console do navegador a cada carregamento da shell.
        return photo is null ? NoContent() : File(photo.Value.Content, photo.Value.ContentType);
    }

    [Authorize]
    [HttpPost("me/photo")]
    public async Task<IActionResult> SetMyPhoto([FromBody] SetPhotoRequest request)
    {
        var result = await profileService.SetPhotoAsync(
            CurrentUsername, request?.ContentType, request?.File);

        return result.Success ? Ok(new { Success = true, result.Message }) : BadRequest(new { message = result.Message });
    }

    [Authorize]
    [HttpDelete("me/photo")]
    public async Task<IActionResult> RemoveMyPhoto()
    {
        var result = await profileService.RemovePhotoAsync(CurrentUsername);

        return result.Success ? Ok(new { Success = true, result.Message }) : BadRequest(new { message = result.Message });
    }

    [Authorize]
    [HttpPut("me/theme")]
    public async Task<IActionResult> SetMyTheme([FromBody] SetThemeRequest request)
    {
        var result = await profileService.SetThemeAsync(CurrentUsername, request?.Theme);

        return result.Success ? Ok(new { Success = true, result.Message }) : BadRequest(new { message = result.Message });
    }

    [Authorize]
    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangeMyPassword([FromBody] ChangePasswordRequest request)
    {
        var result = await profileService.ChangePasswordAsync(
            CurrentUsername,
            request?.CurrentPassword ?? string.Empty,
            request?.NewPassword ?? string.Empty);

        return result.Success ? Ok(new { Success = true, result.Message }) : BadRequest(new { message = result.Message });
    }

    private string CurrentUsername => User.Identity?.Name ?? string.Empty;
}
