using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_DRIVERS")]
public class TruckDriver 
{
    [Key]
    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public required string Document { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public required string Phone { get; set; }

    /// <summary>Número da CNH.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string? LicenseNumber { get; set; }

    /// <summary>Categoria da CNH (A, B, C, D, E, AB, AC, AD, AE).</summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string? LicenseCategory { get; set; }

    /// <summary>Validade da CNH. Vencida só gera aviso no cadastro.</summary>
    [Column(TypeName = "DATE")]
    public DateTime? LicenseExpiresOn { get; set; }

    /// <summary>Inativo some da escolha de motorista.</summary>
    public bool Inactive { get; set; }
}
