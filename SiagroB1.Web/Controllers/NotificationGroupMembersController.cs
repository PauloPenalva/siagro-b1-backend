using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class NotificationGroupMembersController(NotificationGroupMembersService service)
    : ODataBaseController<NotificationGroupMember, Guid>(service)
{
    /// <summary>
    /// Edição parcial da linha.
    ///
    /// A base só expõe <c>Put</c>, que faz <c>State = Modified</c> na entidade recebida: num
    /// PATCH parcial (a tela envia só a coluna alterada) isso marca TODAS as colunas como
    /// modificadas, inclusive a FK, que chega zerada — e o salvamento estoura com violação de
    /// chave estrangeira. Carregar a entidade e aplicar o <see cref="Delta{T}"/> em cima é o
    /// padrão usado em <c>PurchaseContractsController</c>.
    /// </summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<NotificationGroupMember> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var member = await service.GetByIdAsync(key);

        if (member is null)
            return NotFound();

        try
        {
            patch.Patch(member);

            // Normaliza aqui também: editar o telefone sem recalcular PhoneE164 deixaria o
            // membro apontando para o número antigo.
            NotificationGroupMembersService.NormalizePhone(member);
            await service.SaveTrackedChangesAsync();
        }
        catch (DefaultException exception)
        {
            return BadRequest(exception.Message);
        }

        return Updated(member);
    }
}
