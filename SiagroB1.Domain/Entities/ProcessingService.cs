using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// cadastro de tipos de serviço de beneficiamento
/// </summary>
[Table("PROCESSING_SERVICES")]
public class ProcessingService : BaseEntity<string>
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }
}