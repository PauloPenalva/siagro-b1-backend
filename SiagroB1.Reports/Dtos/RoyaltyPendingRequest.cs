// SiagroB1.Reports/Dtos/RoyaltyPendingRequest.cs
namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Royalties ITS" (período = data do romaneio).</summary>
public class RoyaltyPendingRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? CardCode { get; set; }
    public string? OriginCode { get; set; }
    public string? ItemCode { get; set; }
    /// <summary>Nome do membro de RoyaltySituation (ex.: "Declared"); vazio = todas.</summary>
    public string? Situation { get; set; }
    public string? StorageAddressCode { get; set; }
    /// <summary>Nome do membro de RoyaltyBalanceScope ("Pending", "WrittenOff", "All"); vazio = Pending.</summary>
    public string? Scope { get; set; }
}
