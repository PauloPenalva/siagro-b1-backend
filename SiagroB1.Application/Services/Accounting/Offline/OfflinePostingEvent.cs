using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Accounting.Offline;

/// <summary>
/// Um documento pendente de contabilização. <see cref="Codes"/> = códigos com LP ativo e sem lançamento vivo;
/// vazio = só informativo (ex.: cancelado sem efeito líquido). <see cref="EnqueueAsync"/> monta o contexto com o
/// builder do on-line e chama o motor só desses códigos — enfileira, não grava.
/// </summary>
public sealed record OfflinePostingEvent(
    DateTime Date,
    string DocumentType,
    string DocumentKey,
    string DocumentNumber,
    string BranchCode,
    IReadOnlyList<string> Codes,
    string? Note,
    Func<IAccountingPostingService, string, CancellationToken, Task<IReadOnlyList<JournalEntry>>> EnqueueAsync);

public interface IOfflinePostingSource
{
    OfflinePostingModule Module { get; }

    /// <summary>Eventos com data entre <paramref name="from"/> e <paramref name="to"/> (inclusive), da filial resolvida.</summary>
    IAsyncEnumerable<OfflinePostingEvent> CollectAsync(DateTime from, DateTime to, string? branchCode, CancellationToken ct);
}
