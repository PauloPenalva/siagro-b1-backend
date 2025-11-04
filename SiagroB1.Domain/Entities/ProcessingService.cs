using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("processing_services")]
    public class ProcessingService : BaseEntity<string>
    {
        [Column("description")]
        public required string Description { get; set; }

    }
}