using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Configuração de cada uma das 5 dimensões gerenciais (modo STANDALONE).</summary>
[Table("ACCOUNTING_DIMENSIONS")]
public class AccountingDimension
{
    /// <summary>1 a 5. Não é identity.</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Number { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; }

    public DimensionRole Role { get; set; }
}
