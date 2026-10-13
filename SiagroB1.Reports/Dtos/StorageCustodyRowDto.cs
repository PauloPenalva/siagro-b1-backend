namespace SiagroB1.Reports.Dtos;

public static class StorageCustodyRowKind
{
    public const string Romaneio = "Romaneio";
    public const string Step = "Etapa";
}

/// <summary>
/// Linha achatada de "Custódia de Armazenagem": o romaneio (negrito) e, logo abaixo, uma linha por etapa esperada
/// (itálico; "PENDENTE" em negrito quando sem nota). Os saldos do lote se repetem em toda linha do grupo — o rodapé do
/// grupo imprime os da última. Textos já formatados; os decimais só alimentam o rodapé.
/// </summary>
public class StorageCustodyRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = StorageCustodyRowKind.Romaneio;
    public string Transaction { get; set; } = "";
    public string Description { get; set; } = "";
    public string Date { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string Document { get; set; } = "";
    public string TaxDocument { get; set; } = "";
    public string Situation { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public string GrossWeightText { get; set; } = "";
    public string NetWeightText { get; set; } = "";
    public string QuantityText { get; set; } = "";
    public string DifferenceText { get; set; } = "";
    public string LotBalanceLabel { get; set; } = "";
    public decimal LotPhysicalBalance { get; set; }
    public decimal LotFiscalBalance { get; set; }
    public decimal LotDifference { get; set; }
    public int RomaneioCount { get; set; }
    public int PendingCount { get; set; }
}
