using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// CRUD do grupo de notificação. Segue o molde de <c>LogisticRegionService</c>.
/// </summary>
public class NotificationGroupsService(AppDbContext context, ILogger<IBaseService<NotificationGroup, Guid>> logger)
    : BaseService<NotificationGroup, Guid>(context, logger)
{
    /// <summary>
    /// A tela grava grupo, membros e assinaturas num POST só (deep insert), então a
    /// normalização do telefone precisa acontecer AQUI também — passando apenas por
    /// <see cref="NotificationGroupMembersService"/> ela seria pulada, e o membro ficaria com
    /// <c>PhoneE164</c> vazio.
    ///
    /// O efeito seria invisível e grave: o resolver ignora quem não tem telefone normalizado,
    /// de modo que a pessoa apareceria cadastrada na tela e nunca receberia mensagem nenhuma.
    /// </summary>
    public override async Task<NotificationGroup> CreateAsync(NotificationGroup entity)
    {
        NormalizeMembers(entity);

        return await base.CreateAsync(entity);
    }

    public override async Task<NotificationGroup?> UpdateAsync(Guid key, NotificationGroup entity)
    {
        NormalizeMembers(entity);

        return await base.UpdateAsync(key, entity);
    }

    /// <summary>
    /// Persiste alterações já aplicadas a uma entidade RASTREADA (PATCH via <c>Delta</c>).
    ///
    /// O <c>UpdateAsync</c> da base não serve: faz <c>State = Modified</c> e marca também o
    /// <c>RowId</c> de <c>BaseEntity</c>, que é identity — o SQL Server recusa com
    /// "Cannot update identity column 'RowId'".
    /// </summary>
    public async Task SaveTrackedChangesAsync() => await _context.SaveChangesAsync();

    private static void NormalizeMembers(NotificationGroup entity)
    {
        foreach (var member in entity.Members)
        {
            member.PhoneE164 = PhoneNumberNormalizer.ToE164Br(member.Phone)
                ?? throw new DefaultException(
                    $"Telefone inválido para '{member.Name}': '{member.Phone}'. " +
                    "Informe DDD e número, ex.: (66) 99999-8888.");
        }
    }
}
