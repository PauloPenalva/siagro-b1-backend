using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Guarda o resultado econômico do cálculo.
/// </summary>
[Table("STORAGE_CHARGES")]
[Index(nameof(StorageAddressCode), nameof(ChargeType), nameof(PeriodStart), nameof(PeriodEnd), IsUnique = true)]
public class StorageCharge
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressCode { get; set; }

    public StorageChargeType ChargeType { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal BaseQuantity { get; set; }

    [Column(TypeName = "DECIMAL(18,6) NOT NULL")]
    public decimal TonDays { get; set; }

    [Column(TypeName = "DECIMAL(18,8) NOT NULL")]
    public decimal UnitPriceOrRate { get; set; }

    [Column(TypeName = "DECIMAL(18,2) NOT NULL")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,3) NOT NULL")]
    public decimal TotalQuantityLoss { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? ProcessingCostCode { get; set; }

    public DateTime CalculationDate { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    public bool IsInvoiced { get; set; }
}