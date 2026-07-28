using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Membros do grupo. Diferente dos demais cadastros, tem regra própria: o telefone é
/// normalizado na gravação e o cadastro é recusado se não der para determinar o número.
///
/// A validação vive aqui, e não só na tela, porque um telefone inválido não gera erro visível
/// depois — a mensagem simplesmente não chega, e ninguém descobre.
/// </summary>
public class NotificationGroupMembersService(
    AppDbContext context, ILogger<IBaseService<NotificationGroupMember, Guid>> logger)
    : BaseService<NotificationGroupMember, Guid>(context, logger)
{
    /// <summary>
    /// Teto de membros por grupo. Não é limite técnico: é contenção de risco — provedor
    /// não-oficial bane número que dispara em rajada, e cada evento multiplica pelo tamanho
    /// do grupo.
    /// </summary>
    private const int MaxMembersPerGroup = 30;

    public override async Task<NotificationGroupMember> CreateAsync(NotificationGroupMember entity)
    {
        Normalize(entity);
        await EnsureGroupHasRoomAsync(entity);

        return await base.CreateAsync(entity);
    }

    public override async Task<NotificationGroupMember> UpdateAsync(Guid key, NotificationGroupMember entity)
    {
        Normalize(entity);

        return await base.UpdateAsync(key, entity);
    }

    /// <summary>
    /// Persiste alterações já aplicadas a uma entidade RASTREADA — o caso do PATCH, em que o
    /// controller carrega a linha e aplica o <c>Delta</c> em cima dela.
    ///
    /// Não dá para usar o <c>UpdateAsync</c> da base aqui: ele faz <c>State = Modified</c>, que
    /// marca TODAS as colunas como alteradas, inclusive o <c>RowId</c> de
    /// <c>BaseEntity</c> — que é identity, e o SQL Server recusa com
    /// "Cannot update identity column 'RowId'".
    /// </summary>
    public async Task SaveTrackedChangesAsync() => await _context.SaveChangesAsync();

    /// <summary>Normaliza o telefone de um membro já carregado.</summary>
    public static void NormalizePhone(NotificationGroupMember entity) => Normalize(entity);

    private static void Normalize(NotificationGroupMember entity)
    {
        entity.PhoneE164 = PhoneNumberNormalizer.ToE164Br(entity.Phone)
            ?? throw new DefaultException(
                $"Telefone inválido: '{entity.Phone}'. Informe DDD e número, ex.: (66) 99999-8888.");
    }

    private async Task EnsureGroupHasRoomAsync(NotificationGroupMember entity)
    {
        var current = await Task.FromResult(
            _context.Set<NotificationGroupMember>()
                .Count(m => m.NotificationGroupKey == entity.NotificationGroupKey));

        if (current >= MaxMembersPerGroup)
            throw new DefaultException(
                $"O grupo já tem {MaxMembersPerGroup} membros, que é o limite. " +
                "Crie outro grupo para dividir os destinatários.");
    }
}
