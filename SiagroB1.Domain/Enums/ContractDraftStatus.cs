namespace SiagroB1.Domain.Enums;

/// <summary>
/// Ciclo da minuta. Só <see cref="Draft"/> aceita edição/exclusão. Os demais valores são
/// escritos pela Fase 2 (envio, webhook, reconciliação) — existem já para a coluna nascer completa.
/// </summary>
public enum ContractDraftStatus
{
    Draft = 0,
    AwaitingSignature = 1,
    PartiallySigned = 2,
    Signed = 3,
    Canceled = 4,
}
