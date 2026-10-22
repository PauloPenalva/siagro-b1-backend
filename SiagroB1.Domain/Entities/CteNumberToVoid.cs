using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Número de CT-e reservado e não usado, pendente de inutilização na SEFAZ.</summary>
[Table("CTE_NUMBERS_TO_VOID")]
public class CteNumberToVoid
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(14)")] public required string BranchCode { get; set; }
    public int Series { get; set; }
    public int Number { get; set; }
    [Column(TypeName = "VARCHAR(255)")] public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? VoidedAt { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? VoidProtocol { get; set; }
}
