using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_CALCULATION_CHECKPOINTS")]
[Index(nameof(StorageAddressCode), IsUnique = true)]
public class StorageCalculationCheckpoint
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressCode { get; set; }

    public DateTime? LastDailyBalanceDate { get; set; }

    public DateTime? LastStorageChargeDate { get; set; }

    public DateTime? LastTechnicalLossChargeDate { get; set; }

    public DateTime? LastFumigationChargeDate { get; set; }
}