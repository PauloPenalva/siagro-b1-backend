using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("ROYALTY_WRITE_OFF_LINES")]
public class RoyaltyWriteOffLine
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid RoyaltyWriteOffKey { get; set; }
    public RoyaltyWriteOff? RoyaltyWriteOff { get; set; }

    public Guid StorageTransactionKey { get; set; }
    public StorageTransaction? StorageTransaction { get; set; }

    /// <summary>Peso baixado em kg — mesma escala do <see cref="StorageTransaction.NetWeight"/>.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Weight { get; set; }
}
