using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

public class NotificationGroupMembersController(NotificationGroupMembersService service)
    : ODataBaseController<NotificationGroupMember, Guid>(service)
{
    /// <summary>
    /// Inclusão de membro a partir do grupo já gravado.
    ///
    /// A rota precisa ser explícita. A tabela da tela é ligada em <c>rows="{Members}"</c>,
    /// relativa ao contexto do grupo, então o <c>create()</c> do modelo posta em
    /// <c>NotificationGroups(key)/Members</c> — e não na coleção
    /// <c>NotificationGroupMembers</c>. O roteamento por convenção do OData não cobre esse
    /// caminho; como o GET dele já estava declarado à mão em
    /// <see cref="NotificationGroupsController.GetMembers"/>, a URL existia só para GET e o
    /// POST batia em "405 Method Not Allowed".
    ///
    /// Na tela de INCLUSÃO o mesmo botão funcionava, porque ali o grupo ainda não existe e
    /// tudo vai num POST só (deep insert) para <c>NotificationGroups</c> — por isso o erro só
    /// aparecia na edição. Mesmo padrão de <c>PurchaseContractsBrokersController</c>.
    /// </summary>
    [HttpPost("odata/NotificationGroups({key:guid})/Members")]
    [HttpPost("odata/NotificationGroups/{key:guid}/Members")]
    public async Task<IActionResult> PostToMembers(
        [FromRoute] Guid key, [FromBody] NotificationGroupMember member)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // O corpo vem só com as colunas da tabela (Nome/Telefone/Ativo); quem diz a que grupo
        // o membro pertence é a rota.
        member.NotificationGroupKey = key;

        try
        {
            // Passa pelo serviço, e não pelo contexto: é ele que normaliza o telefone e aplica
            // o teto de membros por grupo.
            await service.CreateAsync(member);
        }
        catch (DefaultException exception)
        {
            return BadRequest(exception.Message);
        }

        return Created(member);
    }

    /// <summary>
    /// Leitura de UM membro pelo caminho do grupo.
    ///
    /// Não é rota de tela: é o modelo relendo a linha logo depois de criá-la, para trocar o
    /// registro provisório pelo que o servidor gravou (a chave e o telefone normalizado). Sem
    /// ela o membro entra no banco e mesmo assim a tela mostra "Communication error: 404" — o
    /// erro aparece DEPOIS do "Dados atualizados com sucesso", que é o que torna o sintoma
    /// confuso. A coleção já é servida por <see cref="NotificationGroupsController.GetMembers"/>;
    /// esta é a rota do item.
    /// </summary>
    [HttpGet("odata/NotificationGroups({key:guid})/Members({memberKey:guid})")]
    [HttpGet("odata/NotificationGroups/{key:guid}/Members/{memberKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<NotificationGroupMember>> GetFromMembers(
        [FromRoute] Guid key, [FromRoute] Guid memberKey)
    {
        var member = await service.GetByIdAsync(memberKey);

        return member is null || member.NotificationGroupKey != key ? NotFound() : Ok(member);
    }

    /// <summary>
    /// Exclusão de membro pelo caminho do grupo, que é como a tela apaga a linha selecionada.
    /// Mesmo motivo de <see cref="PostToMembers"/>: sem a rota declarada o DELETE dá 404.
    /// </summary>
    [HttpDelete("odata/NotificationGroups({key:guid})/Members({memberKey:guid})")]
    [HttpDelete("odata/NotificationGroups/{key:guid}/Members/{memberKey:guid}")]
    public async Task<IActionResult> DeleteFromMembers(
        [FromRoute] Guid key, [FromRoute] Guid memberKey)
    {
        var member = await service.GetByIdAsync(memberKey);

        if (member is null || member.NotificationGroupKey != key)
            return NotFound();

        await service.DeleteAsync(memberKey);

        return NoContent();
    }

    /// <summary>
    /// Edição parcial da linha.
    ///
    /// A base só expõe <c>Put</c>, que faz <c>State = Modified</c> na entidade recebida: num
    /// PATCH parcial (a tela envia só a coluna alterada) isso marca TODAS as colunas como
    /// modificadas, inclusive a FK, que chega zerada — e o salvamento estoura com violação de
    /// chave estrangeira. Carregar a entidade e aplicar o <see cref="Delta{T}"/> em cima é o
    /// padrão usado em <c>PurchaseContractsController</c>.
    ///
    /// Precisa ser <c>override</c>, e não um método de mesma assinatura: sem isso o roteamento
    /// continua chamando o <c>Patch</c> da base — que marca a linha inteira como modificada,
    /// inclusive o <c>RowId</c> identity, e devolve 500 ("Cannot update identity column
    /// 'RowId'") ao editar um membro já gravado. Mesma armadilha descrita em
    /// <see cref="NotificationGroupsController.Get"/>.
    /// </summary>
    [AcceptVerbs("PATCH", "MERGE")]
    public override async Task<IActionResult> Patch(
        [FromODataUri] Guid key, [FromBody] Delta<NotificationGroupMember> patch)
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
