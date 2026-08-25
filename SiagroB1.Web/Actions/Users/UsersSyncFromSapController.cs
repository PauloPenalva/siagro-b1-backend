using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.Users;
using SiagroB1.Domain.Constants;

namespace SiagroB1.Web.Actions.Users;

/// <summary>
/// Dispara a sincronização do cadastro de usuários com o SAP sob demanda, sem esperar a varredura
/// periódica.
/// </summary>
public class UsersSyncFromSapController(
    IServiceProvider serviceProvider,
    IUserPermissions userPermissions) : ODataController
{
    [HttpPost("odata/UsersSyncFromSap")]
    public async Task<ActionResult> PostAsync()
    {
        // Alterar o cadastro inteiro de usuários exige o papel ADMIN (perfil/papel, não a flag
        // legada USERS.IsAdmin). Forbid() sem corpo vira "Communication error: 403 Forbidden"
        // genérico no front; devolver a mensagem no corpo, como as demais actions fazem no catch
        // de exceção, deixa o front mostrar o texto real.
        if (!await userPermissions.HasRoleAsync(User.Identity?.Name ?? string.Empty, Roles.Admin))
            return StatusCode(StatusCodes.Status403Forbidden,
                "Você não tem permissão de administrador para sincronizar os usuários com o SAP.");

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
