using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.StorageTransactions;

/// <summary>
/// A quebra técnica automática (GAC-18) pertence ao cálculo diário: nasce junto com a cobrança da
/// quebra (<c>StorageCharge</c>) do período. Cancelar ou estornar só o romaneio deixaria a cobrança
/// viva, e o cálculo nunca o recriaria. Quem apaga e recalcula as duas é "Reprocessar Saldo por Lote".
/// </summary>
public static class AutomaticTechnicalLossGuard
{
    /// <summary>Prefixo do <c>Comments</c> gravado por <c>StorageAddressesTechnicalLossCalculatorService</c>.</summary>
    public const string CommentPrefix = "QUEBRA_TECNICA_";

    public static bool IsAutomatic(StorageTransaction doc) =>
        doc.TransactionType == StorageTransactionType.TechnicalLoss &&
        doc.Comments != null &&
        doc.Comments.StartsWith(CommentPrefix);

    public static void EnsureNotAutomatic(StorageTransaction doc, string action)
    {
        if (IsAutomatic(doc))
            throw new ApplicationException(
                $"O romaneio {doc.Code} é a quebra técnica automática do cálculo diário e não pode ser {action} " +
                "por aqui. Use Reprocessar Saldo por Lote: ele apaga e recalcula as quebras ainda não faturadas do lote.");
    }
}
