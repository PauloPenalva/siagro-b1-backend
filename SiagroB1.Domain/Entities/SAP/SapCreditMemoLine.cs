using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>Linha do A/R Credit Memo do SAP (tabela RIN1). Somente leitura.</summary>
[Table("RIN1")]
public class SapCreditMemoLine
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

    /// <summary>Tipo do documento de origem da linha; 13 = A/R Invoice (OINV). -1 quando não há origem.</summary>
    [Column("BaseType")]
    public int? BaseType { get; set; }

    /// <summary>DocEntry do documento de origem (da OINV quando <see cref="BaseType"/> é 13).</summary>
    [Column("BaseEntry")]
    public int? BaseEntry { get; set; }

    public SapCreditMemo? CreditMemo { get; set; }
}
