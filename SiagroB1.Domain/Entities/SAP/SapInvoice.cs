using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Nota fiscal de saída (A/R Invoice) do SAP Business One (tabela OINV). Somente leitura.
/// Os tipos seguem exatamente o banco de produção: coluna mapeada com tipo errado estoura só
/// contra o banco real, nunca no provider em memória.
/// </summary>
[Table("OINV")]
public class SapInvoice
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

    /// <summary>Moeda do documento (OINV.DocCur); "R$" para reais.</summary>
    [Column("DocCur", TypeName = "NVARCHAR(3)")]
    public string? DocCur { get; set; }

    public ICollection<SapInvoiceLine> Lines { get; set; } = new List<SapInvoiceLine>();

    public SapInvoiceTransport? Transport { get; set; }
}
