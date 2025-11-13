using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("QUALITY_ATTRIBS")]
    public class QualityAttrib : BaseEntity
    {
        [Column(TypeName = "VARCHAR(100) NOT NULL")]
        public required string Name { get; set; }
        
        public bool Disabled { get; set; } = false;
    }
}                          