using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>Linha da A/R Invoice do SAP (tabela INV1). Somente leitura.</summary>
[Table("INV1")]
public class SapInvoiceLine
{
    [Column("DocEntry")]
    public int DocEntry { get; set; }

    [Column("LineNum")]
    public int LineNum { get; set; }

    [Column("ItemCode", TypeName = "NVARCHAR(50)")]
    public string? ItemCode { get; set; }

    [Column("Quantity", TypeName = "NUMERIC(19,6)")]
    public decimal Quantity { get; set; }

    [Column("Price", TypeName = "NUMERIC(19,6)")]
    public decimal Price { get; set; }

    [Column("LineTotal", TypeName = "NUMERIC(19,6)")]
    public decimal LineTotal { get; set; }

    [Column("unitMsr", TypeName = "NVARCHAR(100)")]
    public string? UnitMsr { get; set; }

    [Column("Usage")]
    public int? Usage { get; set; }

    [Column("CFOPCode", TypeName = "NVARCHAR(6)")]
    public string? CfopCode { get; set; }

    public SapInvoice? Invoice { get; set; }
}
