using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Materializa o saldo do lote por dia
/// </summary>
[Table("STORAGE_DAILY_BALANCES")]
[Index(nameof(StorageAddressCode), nameof(BalanceDate), IsUnique = true)]
public class StorageDailyBalance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressCode { get; set; }

    public DateTime BalanceDate { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal OpeningBalance { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal ReceiptQty { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal ShipmentQty { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal TechnicalLossQty { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal ClosingBalance { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal BillableBalance { get; set; }
}