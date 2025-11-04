using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("harvest_season")]
public class HarvestSeason : BaseEntity<string>
{
    [Required(ErrorMessage = "Descrição é obrigatorio.")]
    [Column("harvest_season_name")]
    public required string Name { get; set; }

    [Column("harvest_season_inactive")]
    public bool Inactive { get; set; } = false;
}