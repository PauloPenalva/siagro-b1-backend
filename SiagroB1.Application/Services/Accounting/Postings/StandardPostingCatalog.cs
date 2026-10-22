namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>De onde vêm os dados de um lançamento padrão.</summary>
public enum PostingContextKind
{
    PurchaseInvoice,
    SalesInvoice,
    FinancialDocument,
    Settlement,
    AccountEntry,
    Transfer,
    CostOfSales,
}

/// <summary>
/// Definição fixa de um código de lançamento padrão (o CT5_LANPAD do Protheus): em que evento roda, se itera as
/// linhas do documento, o lote, qual código ele estorna e as fontes que o contador pode escolher nas sequências.
/// </summary>
public sealed record StandardPostingDefinition(
    string Code,
    string Description,
    PostingContextKind Context,
    bool PerLine,
    string Batch,
    string? Reverses,
    string Help,
    IReadOnlyList<string> AccountSources,
    IReadOnlyList<string> ValueSources,
    IReadOnlyList<string> Placeholders);

/// <summary>
/// Catálogo dos códigos de lançamento padrão. Os números seguem o Protheus onde existem (mata103, mata461/521,
/// FINA040/050/070/080/100); 505, 515, 560, 564, 565, 670 e 675 são escolha do projeto (ver a spec).
/// </summary>
public static class StandardPostingCatalog
{
    public const string Fixed = "Fixed";

    private const string PurchaseBatch = "008810";
    private const string SalesBatch = "008820";
    private const string FinancialBatch = "008850";
    private const string CostOfSalesBatch = "008870";

    private static readonly string[] DocumentPlaceholders = ["{DocNumber}", "{PartnerName}", "{PartnerCode}", "{Date}"];
    private static readonly string[] LinePlaceholders = ["{DocNumber}", "{PartnerName}", "{PartnerCode}", "{ItemCode}", "{Date}"];

    private static readonly string[] InvoiceLineAccounts =
        ["Line.LedgerAccount", "Item.LedgerAccount", "Partner.LedgerAccount", "Document.LedgerAccount", Fixed];
    private static readonly string[] InvoiceDocumentAccounts = ["Partner.LedgerAccount", "Document.LedgerAccount", Fixed];

    private static readonly string[] PurchaseLineValues =
    [
        "Line.Total", "Line.GrandTotal", "Line.FiscalStockCost", "Line.IcmsValue", "Line.PisValue", "Line.CofinsValue",
        "Line.FunruralValue", "Line.FreightValue", "Line.DiscountValue", "Line.OtherExpensesValue",
    ];
    private static readonly string[] PurchaseDocumentValues =
    [
        "Document.GrandTotal", "Document.NetPayable", "Document.TotalFunrural", "Document.WithheldIss",
        "Document.WithheldIrrf", "Document.WithheldInss", "Document.WithheldPis", "Document.WithheldCofins",
        "Document.WithheldCsll", "Document.CteIcmsValue",
    ];
    private static readonly string[] SalesLineValues =
    [
        "Line.Total", "Line.GrandTotal", "Line.NetTotal", "Line.IcmsValue", "Line.PisValue", "Line.CofinsValue",
        "Line.FreightValue", "Line.DiscountValue",
    ];
    private static readonly string[] SalesDocumentValues = ["Document.GrandTotal"];

    private static readonly string[] TitleAccounts = ["Partner.LedgerAccount", "Document.LedgerAccount", Fixed];
    private static readonly string[] SettlementAccounts =
    [
        "FinancialAccount.LedgerAccount", "Partner.LedgerAccount", "Document.LedgerAccount",
        "RelatedDocument.LedgerAccount", Fixed,
    ];
    private static readonly string[] SettlementValues =
        ["Settlement.Amount", "Settlement.InterestAmount", "Settlement.FineAmount", "Settlement.DiscountAmount"];
    private static readonly string[] EntryAccounts = ["FinancialAccount.LedgerAccount", "Document.LedgerAccount", Fixed];
    private static readonly string[] TransferAccounts =
        ["FinancialAccount.LedgerAccount", "TargetFinancialAccount.LedgerAccount", Fixed];

    private static StandardPostingDefinition Purchase(string code, string description, bool perLine, string? reverses) =>
        new(code, description, PostingContextKind.PurchaseInvoice, perLine, PurchaseBatch, reverses,
            perLine ? "Roda uma vez por item do Documento de Entrada." : "Roda uma vez por Documento de Entrada.",
            perLine ? InvoiceLineAccounts : InvoiceDocumentAccounts,
            perLine ? PurchaseLineValues : PurchaseDocumentValues,
            perLine ? LinePlaceholders : DocumentPlaceholders);

    private static StandardPostingDefinition Sales(string code, string description, bool perLine, string? reverses) =>
        new(code, description, PostingContextKind.SalesInvoice, perLine, SalesBatch, reverses,
            perLine ? "Roda uma vez por item do Documento de Saída." : "Roda uma vez por Documento de Saída.",
            perLine ? InvoiceLineAccounts : InvoiceDocumentAccounts,
            perLine ? SalesLineValues : SalesDocumentValues,
            perLine ? LinePlaceholders : DocumentPlaceholders);

    private static StandardPostingDefinition Financial(string code, string description, PostingContextKind context,
        string? reverses, string help, IReadOnlyList<string> accounts, IReadOnlyList<string> values) =>
        new(code, description, context, false, FinancialBatch, reverses, help, accounts, values,
            [.. DocumentPlaceholders, "{Description}"]);

    public static IReadOnlyList<StandardPostingDefinition> All { get; } =
    [
        Purchase("650", "Entrada de NF – itens", true, null),
        Purchase("660", "Entrada de NF – total", false, null),
        Purchase("655", "Estorno/cancelamento de entrada de NF – itens", true, "650"),
        Purchase("665", "Estorno/cancelamento de entrada de NF – total", false, "660"),
        Sales("610", "Saída de NF – itens", true, null),
        Sales("620", "Saída de NF – total", false, null),
        Sales("630", "Estorno/cancelamento de saída de NF – itens", true, "610"),
        Sales("635", "Estorno/cancelamento de saída de NF – total", false, "620"),
        Financial("500", "Inclusão de título a receber", PostingContextKind.FinancialDocument, null,
            "Títulos manuais, washout, adiantamentos e desdobramentos. Títulos de nota fiscal não passam aqui.",
            TitleAccounts, ["Document.NetAmount"]),
        Financial("505", "Cancelamento de título a receber", PostingContextKind.FinancialDocument, "500",
            "Estorna o 500 do título.", TitleAccounts, ["Document.NetAmount"]),
        Financial("510", "Inclusão de título a pagar", PostingContextKind.FinancialDocument, null,
            "Títulos manuais, outras compras sem nota, adiantamentos e desdobramentos. Títulos de nota fiscal não passam aqui.",
            TitleAccounts, ["Document.NetAmount"]),
        Financial("515", "Cancelamento de título a pagar", PostingContextKind.FinancialDocument, "510",
            "Estorna o 510 do título.", TitleAccounts, ["Document.NetAmount"]),
        Financial("520", "Baixa a receber", PostingContextKind.Settlement, null,
            "Toda baixa de título a receber, inclusive compensação de adiantamento e encontro de contas.",
            SettlementAccounts, SettlementValues),
        Financial("527", "Estorno de baixa a receber", PostingContextKind.Settlement, "520",
            "Estorna o 520 da baixa.", SettlementAccounts, SettlementValues),
        Financial("530", "Baixa a pagar", PostingContextKind.Settlement, null,
            "Toda baixa de título a pagar, inclusive compensação de adiantamento e encontro de contas.",
            SettlementAccounts, SettlementValues),
        Financial("531", "Estorno de baixa a pagar", PostingContextKind.Settlement, "530",
            "Estorna o 530 da baixa.", SettlementAccounts, SettlementValues),
        Financial("562", "Lançamento na conta financeira – saída", PostingContextKind.AccountEntry, null,
            "Lançamento manual de saída na conta (as pernas de transferência usam o 560).", EntryAccounts, ["Entry.Amount"]),
        Financial("563", "Lançamento na conta financeira – entrada", PostingContextKind.AccountEntry, null,
            "Lançamento manual de entrada na conta (as pernas de transferência usam o 560).", EntryAccounts, ["Entry.Amount"]),
        Financial("564", "Exclusão de lançamento na conta – saída", PostingContextKind.AccountEntry, "562",
            "Estorna o 562.", EntryAccounts, ["Entry.Amount"]),
        Financial("565", "Exclusão de lançamento na conta – entrada", PostingContextKind.AccountEntry, "563",
            "Estorna o 563.", EntryAccounts, ["Entry.Amount"]),
        Financial("560", "Transferência entre contas", PostingContextKind.Transfer, null,
            "FinancialAccount = conta de origem; TargetFinancialAccount = conta de destino.", TransferAccounts,
            ["Transfer.Amount"]),
        Financial("561", "Exclusão de transferência entre contas", PostingContextKind.Transfer, "560",
            "Estorna o 560.", TransferAccounts, ["Transfer.Amount"]),
        new("670", "CMV do período (fechamento do estoque fiscal)", PostingContextKind.CostOfSales, true, CostOfSalesBatch,
            null, "Roda no fechamento do mês do estoque fiscal, uma vez por produto. Pré-requisito: saldo inicial do estoque fiscal lançado.",
            ["Item.LedgerAccount", Fixed], ["Item.CostOfSales"], ["{ItemCode}", "{Date}"]),
        new("675", "Estorno do CMV (reabertura do estoque fiscal)", PostingContextKind.CostOfSales, true, CostOfSalesBatch,
            "670", "Estorna o 670 do mês reaberto.", ["Item.LedgerAccount", Fixed], ["Item.CostOfSales"], ["{ItemCode}", "{Date}"]),
    ];

    private static readonly Dictionary<string, StandardPostingDefinition> ByCode = All.ToDictionary(d => d.Code);

    public static StandardPostingDefinition? Find(string? code) =>
        code is not null && ByCode.TryGetValue(code.Trim(), out var d) ? d : null;
}
