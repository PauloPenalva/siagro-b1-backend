using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Domain.Constants;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Remetente SMTP da empresa (só STANDALONE, só ADMIN). Rotas por atributo no mesmo estilo de
/// BranchNfeSettings: função de leitura + ações de gravar e testar. A senha nunca sai.
/// </summary>
public class EmailSettingsController(IEmailSettingsService service, IUserPermissions userPermissions, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "A configuração de e-mail só existe no modo STANDALONE.";
    private const string AdminOnly = "Somente administradores podem configurar o envio de e-mail.";

    private async Task<IActionResult?> GuardAsync()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(StandaloneOnly);

        // Papel ADMIN via perfil (HasRoleAsync), não a flag IsAdmin do usuário.
        if (!await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin))
            return StatusCode(403, AdminOnly);

        return null;
    }

    [HttpGet("odata/EmailSettingsGet")]
    public async Task<IActionResult> GetAsync()
    {
        if (await GuardAsync() is { } denied) return denied;

        return Ok(await service.GetAsync() ?? new EmailSettingsModel());
    }

    [HttpPost("odata/EmailSettingsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        if (await GuardAsync() is { } denied) return denied;

        if (parameters is null || !parameters.ContainsKey("Host"))
            return BadRequest("Parâmetros obrigatórios: Host, Port, UseStartTls, UserName, Password, FromAddress, FromName, AutoSendEnabled.");

        var model = new EmailSettingsModel
        {
            Host = Text(parameters, "Host") ?? string.Empty,
            Port = parameters.TryGetValue("Port", out var port) && port is not null ? Convert.ToInt32(port) : 587,
            UseStartTls = !parameters.TryGetValue("UseStartTls", out var tls) || tls is null || Convert.ToBoolean(tls),
            UserName = Text(parameters, "UserName"),
            Password = Text(parameters, "Password"),
            FromAddress = Text(parameters, "FromAddress") ?? string.Empty,
            FromName = Text(parameters, "FromName"),
            AutoSendEnabled = parameters.TryGetValue("AutoSendEnabled", out var auto) && auto is not null && Convert.ToBoolean(auto),
        };

        try
        {
            return Ok(await service.SaveAsync(model, User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/EmailSettingsSendTest")]
    public async Task<IActionResult> SendTestAsync([FromBody] ODataActionParameters parameters)
    {
        if (await GuardAsync() is { } denied) return denied;

        if (parameters is null || !parameters.ContainsKey("To"))
            return BadRequest("Parâmetro obrigatório: To.");

        var result = await service.SendTestAsync(Text(parameters, "To") ?? string.Empty);

        return Ok(result);
    }

    private static string? Text(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value) ? value?.ToString() : null;
}
