using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class NotificationGroupsController(
    NotificationGroupsService service,
    NotificationGroupMembersService membersService,
    NotificationGroupSubscriptionsService subscriptionsService)
    : ODataBaseController<NotificationGroup, Guid>(service)
{
    /// <summary>
    /// GET por chave carregando as coleções-filhas.
    ///
    /// O <c>GetByIdAsync</c> herdado usa <c>FindAsync</c>, que não carrega navegações: o
    /// <c>$expand=Members,Subscriptions</c> da tela de edição voltava <c>[]</c> — sem erro
    /// nenhum, as duas abas apenas apareciam vazias. Sem os <c>Include</c> abaixo o expand
    /// não tem o que serializar.
    ///
    /// Precisa ser <c>override</c>, e não um método de mesma assinatura: como a base declara
    /// <c>Get(ID key)</c> e aqui <c>ID</c> é <c>Guid</c>, um método novo apenas esconderia o
    /// herdado e o roteamento continuaria chamando o da base.
    /// </summary>
    [EnableQuery(MaxExpansionDepth = 2)]
    public override async Task<ActionResult<NotificationGroup>> Get([FromRoute] Guid key)
    {
        var group = await service.QueryAll()
            .Include(g => g.Members)
            .Include(g => g.Subscriptions)
            .FirstOrDefaultAsync(g => g.Key == key);

        return group is null ? NotFound() : Ok(group);
    }

    /// <summary>
    /// Edição parcial do cabeçalho do grupo. Mesmo motivo de
    /// <see cref="NotificationGroupMembersController.Patch"/>: o <c>Put</c> da base marca todas
    /// as colunas como modificadas e um PATCH parcial acaba gravando campos em branco.
    /// </summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<NotificationGroup> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var group = await service.GetByIdAsync(key);

        if (group is null)
            return NotFound();

        try
        {
            patch.Patch(group);
            await service.SaveTrackedChangesAsync();
        }
        catch (DefaultException exception)
        {
            return BadRequest(exception.Message);
        }

        return Updated(group);
    }

    /// <summary>
    /// Membros do grupo.
    ///
    /// A rota precisa ser explícita: o roteamento por convenção do OData procura um
    /// <c>GetMembers</c> no controller e, sem ele, <c>NotificationGroups(key)/Members</c>
    /// devolve 404 — o salvamento acusa "Communication error: 404" logo depois de gravar
    /// com sucesso. Mesmo padrão de <see cref="PurchaseContractsChangeLogsController"/>.
    /// </summary>
    [HttpGet("odata/NotificationGroups({key:guid})/Members")]
    [HttpGet("odata/NotificationGroups/{key:guid}/Members")]
    [EnableQuery]
    public ActionResult<IEnumerable<NotificationGroupMember>> GetMembers([FromRoute] Guid key) =>
        Ok(membersService.QueryAll().Where(member => member.NotificationGroupKey == key));

    /// <summary>Eventos assinados pelo grupo. Mesmo motivo de <see cref="GetMembers"/>.</summary>
    [HttpGet("odata/NotificationGroups({key:guid})/Subscriptions")]
    [HttpGet("odata/NotificationGroups/{key:guid}/Subscriptions")]
    [EnableQuery]
    public ActionResult<IEnumerable<NotificationGroupSubscription>> GetSubscriptions([FromRoute] Guid key) =>
        Ok(subscriptionsService.QueryAll().Where(s => s.NotificationGroupKey == key));
}
