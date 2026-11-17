using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Nota fiscal de devolução de venda (A/R Credit Memo) do SAP Business One (tabela ORIN). Somente leitura.
/// ORIN tem o mesmo esquema de cabeçalho da OINV; os tipos repetem os de <see cref="SapInvoice"/>.
/// O DocEntry é uma sequência PRÓPRIA de ORIN: o mesmo número pode existir em OINV para outro documento.
/// </summary>
[Table("ORIN")]
public class SapCreditMemo
{
    [Key]
    [Column("DocEntry")]
    public int DocEntry { get; set; }

    [Column("DocNum")]
    public int DocNum { get; set; }

    [Column("DocDate", TypeName = "DATETIME")]
    public DateTime DocDate { get; set; }

    [Column("CardCode", TypeName = "NVARCHAR(15)")]
    public string? CardCode { get; set; }

    [Column("CardName", TypeName = "NVARCHAR(200)")]
    public string? CardName { get; set; }

    [Column("BPLId")]
    public int? BPLId { get; set; }

    /// <summary>CANCELED: 'N' normal, 'Y' cancelada, 'C' é o documento de cancelamento.</summary>
    [Column("CANCELED", TypeName = "CHAR(1)")]
    public string? Canceled { get; set; }

    /// <summary>Número da NF-e (Serial).</summary>
    [Column("Serial")]
    public int? Serial { get; set; }

    [Column("SeriesStr", TypeName = "NVARCHAR(3)")]
    public string? SeriesStr { get; set; }

    /// <summary>Modelo do documento fiscal ('39' = NF-e).</summary>
    [Column("Model", TypeName = "NVARCHAR(6)")]
    public string? Model { get; set; }

    public ICollection<SapCreditMemoLine> Lines { get; set; } = new List<SapCreditMemoLine>();
}
