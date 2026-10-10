using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Filtros comuns da lista de pendentes, dos totais e do PDF de royalties ITS.</summary>
public class RoyaltyBalanceFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? CardCode { get; set; }
    public string? OriginCode { get; set; }
    public string? ItemCode { get; set; }
    public RoyaltySituation? Situation { get; set; }
    public string? StorageAddressCode { get; set; }
    /// <summary>Só romaneios com saldo &gt; 0 (padrão).</summary>
    public bool PendingOnly { get; set; } = true;
}
