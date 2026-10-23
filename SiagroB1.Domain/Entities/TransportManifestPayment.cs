using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Pagamento do frete (infPag).</summary>
[Table("TRANSPORT_MANIFEST_PAYMENTS")]
public class TransportManifestPayment
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    [Column(TypeName = "VARCHAR(14)")] public required string ContractorTaxId { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public required string ContractorName { get; set; }
    [Column(TypeName = "DECIMAL(15,2)")] public decimal Total { get; set; }
    /// <summary>indPag: 0 à vista, 1 a prazo (com parcelas).</summary>
    public int IndPag { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Pix { get; set; }
    [Column(TypeName = "VARCHAR(5)")] public string? BankCode { get; set; }
    [Column(TypeName = "VARCHAR(10)")] public string? Agency { get; set; }

    public virtual ICollection<TransportManifestPaymentComponent> Components { get; set; } = new List<TransportManifestPaymentComponent>();
    public virtual ICollection<TransportManifestPaymentInstallment> Installments { get; set; } = new List<TransportManifestPaymentInstallment>();
}
