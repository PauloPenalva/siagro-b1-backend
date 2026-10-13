using SiagroB1.Domain.Dtos;

namespace SiagroB1.Domain.Enums;

/// <summary>
/// Regras puras das notas de custódia em armazém próprio (spec 2026-10-12 §3.4, §4.2). Usadas pela guarda e pela seção
/// do romaneio (Application) e pelo relatório (Reports): a mesma regra nos dois lados.
/// </summary>
public static class StorageCustodyRules
{
    /// <summary>
    /// Etapas que o romaneio espera, em ordem. Só romaneio Confirmado/Faturado de lote de terceiro Regular. Romaneios de
    /// transferência CANCELADA — os originais e as contrapartidas que o cancelamento cria, todos com o mesmo
    /// OwnershipTransferKey — não esperam nada.
    /// </summary>
    public static IReadOnlyList<StorageCustodyRole> ExpectedRoles(StorageCustodyTransactionRow t)
    {
        if (t.OwnershipType != StorageOwnershipType.ThirdParty || t.Nature != StorageAddressNature.Regular)
            return [];

        if (t.TransactionStatus is not (StorageTransactionsStatus.Confirmed or StorageTransactionsStatus.Invoiced))
            return [];

        if (t.TransferCancelled)
            return [];

        return t.TransactionType switch
        {
            StorageTransactionType.Receipt when t.IsOwnershipTransfer => [StorageCustodyRole.StorageEntry],
            StorageTransactionType.Receipt when t.RequiresProcessing =>
                [StorageCustodyRole.ProcessingEntry, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodyRole.StorageEntry],
            StorageTransactionType.Receipt => [StorageCustodyRole.StorageEntry],
            StorageTransactionType.Shipment when t.IsOwnershipTransfer => [StorageCustodyRole.StorageSymbolicReturn],
            StorageTransactionType.Shipment => [StorageCustodyRole.StorageReturn],
            _ => [],
        };
    }

    /// <summary>Entradas: Documento de Entrada (própria ou escrituração).</summary>
    public static bool IsPurchaseRole(StorageCustodyRole role) =>
        role is StorageCustodyRole.ProcessingEntry or StorageCustodyRole.StorageEntry;

    /// <summary>Devolução e retornos: Documento de Saída, sempre próprio.</summary>
    public static bool IsSalesRole(StorageCustodyRole role) =>
        role is StorageCustodyRole.ProcessingSymbolicReturn or StorageCustodyRole.StorageReturn or StorageCustodyRole.StorageSymbolicReturn;

    /// <summary>
    /// Bruto / bruto / líquido (spec D7): entrada para beneficiamento pelo bruto; devolução simbólica pela quantidade da
    /// nota do papel 1 (null sem ela); armazenagem e retornos pelo líquido, que é o que o saldo do lote soma.
    /// </summary>
    public static decimal? ExpectedQuantity(StorageCustodyRole role, StorageCustodyTransactionRow t, decimal? processingEntryQuantity) =>
        role switch
        {
            StorageCustodyRole.ProcessingEntry => t.GrossWeight,
            StorageCustodyRole.ProcessingSymbolicReturn => processingEntryQuantity,
            StorageCustodyRole.None => null,
            _ => t.NetWeight,
        };

    public static StorageCustodyStepSituation Situation(StorageCustodyDocumentRow? d)
    {
        if (d is null)
            return StorageCustodyStepSituation.Missing;

        if (d.IsSalesInvoice || d.IssuerType == DocumentIssuerType.Own)
            return d.NfeStatus == NfeStatus.Authorized ? StorageCustodyStepSituation.Authorized : StorageCustodyStepSituation.Generated;

        return d.InvoiceStatus == InvoiceStatus.Confirmed ? StorageCustodyStepSituation.Recorded : StorageCustodyStepSituation.Generated;
    }

    public static string RoleText(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => "Entrada para beneficiamento",
        StorageCustodyRole.ProcessingSymbolicReturn => "Devolução simbólica do beneficiamento",
        StorageCustodyRole.StorageEntry => "Entrada para armazenagem",
        StorageCustodyRole.StorageReturn => "Retorno de armazenagem",
        StorageCustodyRole.StorageSymbolicReturn => "Retorno simbólico de armazenagem",
        _ => "Nenhum",
    };

    /// <summary>Rótulo curto do relatório (cabe na coluna).</summary>
    public static string ShortRoleText(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => "Entrada beneficiamento",
        StorageCustodyRole.ProcessingSymbolicReturn => "Devolução simbólica",
        StorageCustodyRole.StorageEntry => "Entrada armazenagem",
        StorageCustodyRole.StorageReturn => "Retorno",
        StorageCustodyRole.StorageSymbolicReturn => "Retorno simbólico",
        _ => "",
    };

    public static string SituationText(StorageCustodyStepSituation situation) => situation switch
    {
        StorageCustodyStepSituation.Generated => "Gerada",
        StorageCustodyStepSituation.Authorized => "Autorizada",
        StorageCustodyStepSituation.Recorded => "Escriturada",
        _ => "PENDENTE",
    };

    /// <summary>Número da NF quando existe; senão o interno.</summary>
    public static string DocumentNumber(StorageCustodyDocumentRow d) =>
        string.IsNullOrWhiteSpace(d.TaxDocumentNumber) ? d.InvoiceNumber ?? "" : d.TaxDocumentNumber;

    public static List<StorageCustodyStepDto> BuildSteps(StorageCustodyTransactionRow t, IEnumerable<StorageCustodyDocumentRow> documents)
    {
        var mine = documents.Where(d => d.TransactionKey == t.Key).ToList();
        var processingEntryQuantity = mine.FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry)?.Quantity;

        return ExpectedRoles(t).Select(role =>
        {
            var doc = mine.FirstOrDefault(d => d.Role == role);
            var expected = ExpectedQuantity(role, t, processingEntryQuantity);
            var situation = Situation(doc);

            return new StorageCustodyStepDto
            {
                Role = role,
                RoleText = RoleText(role),
                Situation = situation,
                SituationText = SituationText(situation),
                DocumentKey = doc?.DocumentKey,
                IsSalesInvoice = doc?.IsSalesInvoice ?? IsSalesRole(role),
                InvoiceNumber = doc?.InvoiceNumber,
                Number = doc is null ? null : DocumentNumber(doc),
                TaxDocumentSeries = doc?.TaxDocumentSeries,
                ChaveNFe = doc?.ChaveNFe,
                IssueDate = doc?.IssueDate,
                Cfop = doc?.Cfop,
                NfeStatus = doc?.NfeStatus,
                Quantity = doc?.Quantity,
                ExpectedQuantity = expected,
                Difference = doc is not null && expected is not null ? doc.Quantity - expected : null,
            };
        }).ToList();
    }

    public static StorageCustodyRole NextRole(IEnumerable<StorageCustodyStepDto> steps) =>
        steps.FirstOrDefault(s => s.Situation == StorageCustodyStepSituation.Missing)?.Role ?? StorageCustodyRole.None;
}
