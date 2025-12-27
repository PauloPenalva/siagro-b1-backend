using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Security.Services;

namespace SiagroB1.Gateway.Controllers;

[ApiController]
[Route("security/users")]
public class UserController(UserService service) : ControllerBase
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
}