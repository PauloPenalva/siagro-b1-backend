using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// XMLs da NF-e do documento. O assinado é gravado ANTES do envio: sem ele não há como montar o
/// procNFe se a resposta da SEFAZ se perder. Não é exposto no OData (download por função própria).
/// </summary>
[Table("SALES_INVOICE_NFE_XMLS")]
public class SalesInvoiceNfeXml
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(SalesInvoice))]
    public Guid SalesInvoiceKey { get; set; }

    public virtual SalesInvoice? SalesInvoice { get; set; }

    public SalesInvoiceNfeXmlKind Kind { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public required string Xml { get; set; }

    public DateTime CreatedAt { get; set; }
}
