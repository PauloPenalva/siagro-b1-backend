using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Saldo inicial valorizado do estoque fiscal por filial + produto (Kardex Fiscal, spec 2026-10-20 item 4).
/// É a camada PEPS mais antiga, na data do saldo; movimentos até essa data (inclusive) são ignorados.
/// Um lançamento por filial + produto (índice único em AppDbContext).
/// </summary>
[Table("FISCAL_INVENTORY_OPENING_BALANCES")]
public class FiscalInventoryOpeningBalance : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string BranchCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string ItemCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    public DateTime BalanceDate { get; set; }

    [Column(TypeName = "DECIMAL(19,6)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(19,2)")]
    public decimal TotalValue { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? UnitOfMeasureCode { get; set; }
}
