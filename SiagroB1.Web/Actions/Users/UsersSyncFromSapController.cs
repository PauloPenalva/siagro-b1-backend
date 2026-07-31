using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Users;
using SiagroB1.Web.Extensions;

namespace SiagroB1.Web.Actions.Users;

/// <summary>
/// Dispara a sincronização do cadastro de usuários com o SAP sob demanda, sem esperar a varredura
/// periódica.
/// </summary>
public class UsersSyncFromSapController(IServiceProvider serviceProvider) : ODataController
{
    [HttpPost("odata/UsersSyncFromSap")]
    public async Task<ActionResult> PostAsync()
    {
        // Alterar o cadastro inteiro de usuários é operação de administrador.
        if (!User.IsAdmin())
            return Forbid();

        // O serviço só é registrado quando Erp = SAPB1: resolver pelo provider (em vez de injetar
        // no construtor) evita que a action deixe de existir - e devolva 404 - no modo standalone.
        var service = serviceProvider.GetService<SapUserSyncService>();

        if (service is null)
            return BadRequest("A sincronização de usuários só está disponível na integração com o SAP Business One.");

        try
        {
            var result = await service.ExecuteAsync();

            return Ok(new
            {
                result.Created,
                result.Updated,
                result.Deactivated,
                result.EmailsDiscarded,
                Message = $"{result.Created} usuário(s) criado(s), {result.Updated} atualizado(s) " +
                          $"e {result.Deactivated} desativado(s)."
            });
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
