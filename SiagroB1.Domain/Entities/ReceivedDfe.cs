using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento fiscal eletrônico destinado à filial, obtido pela Distribuição de DF-e da SEFAZ (só STANDALONE).
/// Único por (BranchCode, AccessKey). O XML completo só existe depois da ciência/confirmação.
/// </summary>
[Table("RECEIVED_DFES")]
public class ReceivedDfe
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public required string BranchCode { get; set; }

    [Column(TypeName = "VARCHAR(44)")]
    public required string AccessKey { get; set; }

    public long Nsu { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string? IssuerTaxId { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? IssuerName { get; set; }

    public DateTimeOffset? IssueDate { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? TotalValue { get; set; }

    public DfeInvoiceSituation Situation { get; set; } = DfeInvoiceSituation.Authorized;

    public DfeManifestationStatus ManifestationStatus { get; set; } = DfeManifestationStatus.None;

    public DateTimeOffset? ManifestedAt { get; set; }

    [Column(TypeName = "VARCHAR(30)")]
    public string? ManifestationProtocol { get; set; }

    [Column(TypeName = "VARCHAR(255)")]
    public string? ManifestationJustification { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? ManifestationProcEventXml { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? Xml { get; set; }

    public DateTimeOffset? XmlDownloadedAt { get; set; }

    /// <summary>Documento de Entrada gerado a partir deste DF-e, quando houver.</summary>
    public int? PurchaseInvoiceDocEntry { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
