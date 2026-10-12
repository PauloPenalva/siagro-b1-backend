namespace SiagroB1.Reports.Dtos;

/// <summary>Tipo da linha achatada do relatório (string: o FastReport não precisa converter enum).</summary>
public static class StorageRemittanceRowKind
{
    public const string Remittance = "Remessa";
    public const string Return = "Retorno";
    public const string Transaction = "Romaneio";
}

/// <summary>
/// Linha de "Remessas para Depósito", achatada. Remessa: todas as colunas. Retorno (sempre, logo abaixo da remessa):
/// documento de entrada, NF, emissão, situação, NF-e, chave, <see cref="Note"/> com os romaneios conciliados, a
/// quantidade em Retornado e o saldo depois dele em A retornar. Romaneio (só com Detalhado): só <see cref="Detail"/>.
/// As quantidades decimais valem 0 fora da remessa — são elas que os totais do template somam; os textos são o que a
/// célula imprime.
/// </summary>
public class StorageRemittanceRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = StorageRemittanceRowKind.Remittance;
    /// <summary>Número interno: DS… na remessa, DE… no retorno.</summary>
    public string Document { get; set; } = "";
    /// <summary>Número da nota fiscal com a série ("000000038-1"); vazio sem nota.</summary>
    public string TaxDocument { get; set; } = "";
    /// <summary>Chave da NF-e em grupos de 4; vazia sem chave (a linha da chave some no PDF).</summary>
    public string AccessKey { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Contract { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    /// <summary>Só no retorno: romaneios conciliados com ele.</summary>
    public string Note { get; set; } = "";
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
