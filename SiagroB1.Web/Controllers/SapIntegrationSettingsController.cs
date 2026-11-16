using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Constants;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Tela "Integração SAP" (só SAPB1, só ADMIN): liga/desliga e intervalo da sincronização do cancelamento das
/// invoices vinculadas (GAC-1226), mais o botão "Sincronizar agora". Mesmo estilo do EmailSettings: função de
/// leitura e ações, sem entity set.
/// </summary>
public class SapIntegrationSettingsController(
    SapIntegrationSettingsService settings,
    IServiceProvider services,
    IUserPermissions userPermissions,
    IConfiguration configuration) : ODataController
{
    private const string SapOnly = "Disponível somente com integração SAP Business One.";
    private const string AdminOnly = "Somente administradores podem configurar a integração com o SAP.";

    private async Task<IActionResult?> GuardAsync()
    {
        if (!ErpMode.IsSapB1(configuration)) return BadRequest(SapOnly);

        // Papel ADMIN via perfil (HasRoleAsync), não a flag IsAdmin do usuário.
        if (!await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin))
            return StatusCode(403, AdminOnly);

        return null;
    }

    [HttpGet("odata/SapIntegrationSettingsGet")]
    public async Task<IActionResult> GetAsync()
    {
        if (await GuardAsync() is { } denied) return denied;

        return Ok(await settings.GetAsync());
    }

    [HttpPost("odata/SapIntegrationSettingsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        if (await GuardAsync() is { } denied) return denied;

        if (parameters is null
            || !parameters.TryGetValue("InvoiceSyncEnabled", out var enabled) || enabled is null
            || !parameters.TryGetValue("InvoiceSyncIntervalMinutes", out var interval) || interval is null)
            return BadRequest("Parâmetros obrigatórios: InvoiceSyncEnabled, InvoiceSyncIntervalMinutes.");

        try
        {
            // A última execução não é parâmetro: só o serviço de sincronização a grava.
            return Ok(await settings.SaveAsync(new SapIntegrationSettingsModel
            {
                InvoiceSyncEnabled = Convert.ToBoolean(enabled),
                InvoiceSyncIntervalMinutes = Convert.ToInt32(interval),
            }));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/SapInvoiceSyncRunNow")]
    public async Task<IActionResult> RunNowAsync(CancellationToken ct)
    {
        // O serviço só é registrado no ramo SAPB1 do Program (precisa do SapErpDbContext).
        var sync = services.GetService<SapInvoiceSyncService>();
        if (sync is null) return BadRequest(SapOnly);

        if (await GuardAsync() is { } denied) return denied;

        await sync.ExecuteAsync(ct, force: true);

        return Ok(await settings.GetAsync());
    }
}
