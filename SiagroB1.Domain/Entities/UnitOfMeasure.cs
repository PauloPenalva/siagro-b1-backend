using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("units_of_measure")]
    public class UnitOfMeasure : BaseEntity<string>
    {
        [Column("description")]
        public required string Description { get; set; }
    }
}