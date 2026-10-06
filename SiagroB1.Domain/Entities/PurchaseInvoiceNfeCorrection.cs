using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// CC-e registrada sobre a NF-e do documento de entrada (spec 2026-10-06). Só nasce com a carta registrada
/// (135/155) — recusa e falta de resposta não gravam. Somente leitura no OData; o procEventoNFe sai do EDM
/// e é baixado pela function própria.
/// </summary>
[Table("PURCHASE_INVOICE_NFE_CORRECTIONS")]
public class PurchaseInvoiceNfeCorrection : INfeCorrection
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(PurchaseInvoice))]
    public Guid PurchaseInvoiceKey { get; set; }

    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    /// <summary>nSeqEvento (1–20); único por documento.</summary>
    public int Sequence { get; set; }

    [Column(TypeName = "NVARCHAR(1000)")]
    public required string Text { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? Protocol { get; set; }

    /// <summary>dhRegEvento em hora de Brasília (mesmo tratamento de NfeCancelledAt).</summary>
    public DateTime? RegisteredAt { get; set; }

    public int StatusCode { get; set; }

    [Column(TypeName = "VARCHAR(255)")]
    public required string Reason { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? ProcEventXml { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
