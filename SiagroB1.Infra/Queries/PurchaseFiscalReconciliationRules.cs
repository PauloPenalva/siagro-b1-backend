using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Regras puras da Conciliação Fiscal de Compras (spec 2026-10-13 §4). Moram no Infra porque a tela (Web, via
/// Application) e o relatório (Reports, que não referencia a Application) precisam das mesmas.
/// </summary>
public static class PurchaseFiscalReconciliationRules
{
    /// <summary>Unidade física do romaneio — a do vínculo. Ver <see cref="ItemComplement.CommercialFactor"/>.</summary>
    public const string BaseUnit = "KG";

    public static readonly StorageTransactionType[] LinkableTypes =
    [
        StorageTransactionType.Purchase,
        StorageTransactionType.PurchaseReturn,
        StorageTransactionType.PurchaseQtyComplement,
        StorageTransactionType.PurchaseDischargeLoss,
        StorageTransactionType.PurchaseDischargeGain,
    ];

    public static bool IsLinkable(StorageTransactionType type) => LinkableTypes.Contains(type);

    /// <summary>NF de devolução cobre o que SAI do contrato (9, 16); a normal, o que ENTRA (8, 10, 17).</summary>
    public static bool SenseMatches(PurchaseInvoiceType invoiceType, StorageTransactionType type) =>
        invoiceType == PurchaseInvoiceType.Return
            ? type is StorageTransactionType.PurchaseReturn or StorageTransactionType.PurchaseDischargeLoss
            : type is StorageTransactionType.Purchase or StorageTransactionType.PurchaseQtyComplement
                or StorageTransactionType.PurchaseDischargeGain;

    /// <summary>Dono do documento: CPF inteiro (11) ou raiz do CNPJ (8 primeiros de 14). Outra coisa: null.</summary>
    public static string? OwnerRoot(string? taxId)
    {
        var digits = new string((taxId ?? "").Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            14 => digits[..8],
            11 => digits,
            _ => null,
        };
    }

    /// <summary>Produtor com 2+ inscrições = 2+ parceiros com o mesmo CPF/raiz de CNPJ (D6).</summary>
    public static bool SameOwner(string? a, string? b)
    {
        var root = OwnerRoot(a);
        return root != null && root == OwnerRoot(b);
    }

    /// <summary>
    /// KG por unidade da linha da NF. KG (ou vazio) = 1; a UM comercial do produto = o fator dela; qualquer outra
    /// coisa = null (o vínculo é recusado e a linha aparece sem Disponível).
    /// </summary>
    public static decimal? KgFactor(string? lineUnit, ItemComplement? complement)
    {
        if (string.IsNullOrWhiteSpace(lineUnit) || string.Equals(lineUnit.Trim(), BaseUnit, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (complement is { CommercialFactor: > 0 } &&
            string.Equals(lineUnit.Trim(), complement.CommercialUnitOfMeasureCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            return complement.CommercialFactor;

        return null;
    }

    public static FiscalReconciliationStatus Status(decimal physical, decimal fiscal, bool hasLinks, bool confirmed)
    {
        if (!hasLinks)
            return FiscalReconciliationStatus.WithoutInvoice;

        return fiscal == physical || confirmed
            ? FiscalReconciliationStatus.Reconciled
            : FiscalReconciliationStatus.Partial;
    }

    /// <summary>Mesmos rótulos de <c>formatter.formatStorageTransactionType</c> no frontend.</summary>
    public static string TypeName(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Purchase => "Compra",
        StorageTransactionType.PurchaseReturn => "Dev.Compra",
        StorageTransactionType.PurchaseQtyComplement => "Compl.Qtd.",
        StorageTransactionType.PurchaseDischargeLoss => "Quebra Descarga",
        StorageTransactionType.PurchaseDischargeGain => "Sobra Descarga",
        _ => type.ToString(),
    };

    public static string StatusName(FiscalReconciliationStatus status) => status switch
    {
        FiscalReconciliationStatus.WithoutInvoice => "Sem NF",
        FiscalReconciliationStatus.Partial => "Parcial",
        _ => "Conciliado",
    };

    /// <summary>"número-série" da NF; sem número fiscal, o número interno do documento.</summary>
    public static string TaxDocument(string? taxNumber, string? series, string? internalNumber)
    {
        if (string.IsNullOrWhiteSpace(taxNumber))
            return internalNumber?.Trim() ?? "";

        return string.IsNullOrWhiteSpace(series) ? taxNumber.Trim() : $"{taxNumber.Trim()}-{series.Trim()}";
    }
}
