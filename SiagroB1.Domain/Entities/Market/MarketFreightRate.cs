using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Frete por tonelada de uma cidade até um destino, com vigência. Vale o de maior ValidFrom já iniciado.</summary>
[Table("MARKET_FREIGHT_RATES")]
[Index(nameof(MunicipalityCode), nameof(DestinationId), nameof(ValidFrom), IsUnique = true)]
public class MarketFreightRate
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(7) NOT NULL")]
    public required string MunicipalityCode { get; set; }

    [ForeignKey(nameof(MunicipalityCode))]
    public Municipality? Municipality { get; set; }

    public int DestinationId { get; set; }

    [ForeignKey(nameof(DestinationId))]
    public MarketDestination? Destination { get; set; }

    /// <summary>R$ por tonelada.</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal RatePerTon { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime ValidFrom { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
