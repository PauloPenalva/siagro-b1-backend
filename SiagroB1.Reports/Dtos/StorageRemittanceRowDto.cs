namespace SiagroB1.Reports.Dtos;

/// <summary>Tipo da linha achatada do relatório (string: o FastReport não precisa converter enum).</summary>
public static class StorageRemittanceRowKind
{
    public const string Remittance = "Remessa";
    public const string Return = "Retorno";
    public const string Transaction = "Romaneio";
}

/// <summary>
/// Linha de "Remessas para Depósito". Na remessa, as colunas vêm preenchidas e <see cref="Detail"/> vazio; nas
/// filhas (Detalhado), só <see cref="Detail"/>. As quantidades decimais valem 0 nas filhas — são elas que os
/// totais do template somam; os textos são o que a célula imprime (vazios nas filhas).
/// </summary>
public class StorageRemittanceRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = StorageRemittanceRowKind.Remittance;
    public string Remittance { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Contract { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string Detail { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public string RemittedText { get; set; } = "";
    public string ReturnedText { get; set; } = "";
    public string PendingReturnText { get; set; } = "";
    public string ShippedWithoutReturnText { get; set; } = "";
    public decimal RemittedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal PendingReturnQuantity { get; set; }
    public decimal ShippedWithoutReturnQuantity { get; set; }
    public int RemittanceCount { get; set; }
}
