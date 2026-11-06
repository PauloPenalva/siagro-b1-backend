using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Destino (porto, indústria) para o qual o frete e o preço de origem são calculados.</summary>
[Table("MARKET_DESTINATIONS")]
[Index(nameof(Name), IsUnique = true)]
public class MarketDestination
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public bool Active { get; set; } = true;
}
