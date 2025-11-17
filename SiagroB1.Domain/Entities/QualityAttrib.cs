using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("QUALITY_ATTRIBS")]
    public class QualityAttrib 
    {
        [Key]
        [Column(TypeName = "VARCHAR(10) NOT NULL")]
        public required string Code { get; set; }
        
        [Column(TypeName = "VARCHAR(100) NOT NULL")]
        public required string Name { get; set; }
        
        public bool Disabled { get; set; } 
    }
}                          