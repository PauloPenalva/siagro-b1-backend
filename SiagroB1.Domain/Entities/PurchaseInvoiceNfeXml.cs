using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// XMLs da NF-e do documento de entrada — espelho de <see cref="SalesInvoiceNfeXml"/>. O assinado é gravado
/// ANTES do envio. Não é exposto no OData (download por função própria).
/// </summary>
[Table("PURCHASE_INVOICE_NFE_XMLS")]
public class PurchaseInvoiceNfeXml
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(PurchaseInvoice))]
    public Guid PurchaseInvoiceKey { get; set; }

    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    public NfeXmlKind Kind { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public required string Xml { get; set; }

    public DateTime CreatedAt { get; set; }
}
