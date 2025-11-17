using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("BRANCHS")]
    public class Branch
    {
        [Key]
        [Column(TypeName = "VARCHAR(14) NOT NULL", Order = 1)]
        public string? Code { get; set; }

        [Column(TypeName = "VARCHAR(100) NOT NULL")]
        public required string BranchName { get; set; }
    }
}