using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento fiscal transportado por um CT-e de frete (<c>infDoc</c>), identificado pela chave de acesso. Quando a
/// chave casa com uma NF de venda ou uma entrada do sistema, o vínculo é gravado para o rateio e a conferência.
/// </summary>
[Table("PURCHASE_INVOICE_TRANSPORTED_DOCUMENTS")]
public class PurchaseInvoiceTransportedDocument
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(PurchaseInvoice))]
    public Guid PurchaseInvoiceKey { get; set; }

    /// <summary>O CT-e.</summary>
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [Column(TypeName = "VARCHAR(44) NOT NULL")]
    public required string AccessKey { get; set; }

    [ForeignKey(nameof(SalesInvoice))]
    public Guid? SalesInvoiceKey { get; set; }

    public virtual SalesInvoice? SalesInvoice { get; set; }

    [ForeignKey(nameof(TransportedPurchaseInvoice))]
    public Guid? TransportedPurchaseInvoiceKey { get; set; }

    public virtual PurchaseInvoice? TransportedPurchaseInvoice { get; set; }
}
