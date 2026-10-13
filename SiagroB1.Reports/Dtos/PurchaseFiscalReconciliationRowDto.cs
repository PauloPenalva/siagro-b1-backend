namespace SiagroB1.Reports.Dtos;

public static class PurchaseFiscalReconciliationRowKind
{
    public const string Transaction = "Romaneio";
    public const string Invoice = "NF";
    public const string Unlinked = "NF livre";
}

/// <summary>
/// Linha de "Entregas × NF do Fornecedor". Romaneio = linha cheia; NF vinculada = linha em itálico logo abaixo;
/// NF livre = seção final. <c>Group*</c> soma no subtotal do contrato; <c>Sum*</c> só na primeira aparição do romaneio
/// (rateado em 2 contratos aparece nos 2, mas o total geral conta uma vez).
/// </summary>
public class PurchaseFiscalReconciliationRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = PurchaseFiscalReconciliationRowKind.Transaction;
    public string Document { get; set; } = "";
    public string Date { get; set; } = "";
    public string Description { get; set; } = "";
    public string Party { get; set; } = "";
    public string PhysicalText { get; set; } = "";
    public string FiscalText { get; set; } = "";
    public string DifferenceText { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal GroupPhysical { get; set; }
    public decimal GroupFiscal { get; set; }
    public decimal GroupDifference { get; set; }
    public decimal SumPhysical { get; set; }
    public decimal SumFiscal { get; set; }
    public decimal SumDifference { get; set; }
    public int TransactionCount { get; set; }
}
