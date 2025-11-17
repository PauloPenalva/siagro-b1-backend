using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// cadastro de tipos de serviço de beneficiamento
/// </summary>
[Table("PROCESSING_SERVICES")]
public class ProcessingService
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }
}