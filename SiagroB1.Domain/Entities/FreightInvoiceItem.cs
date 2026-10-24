using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>CT-e incluído na fatura de frete. Um CT-e só pode estar em uma fatura ativa por vez.</summary>
[Table("FREIGHT_INVOICE_ITEMS")]
public class FreightInvoiceItem
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid InvoiceKey { get; set; }
    public virtual FreightInvoice? Invoice { get; set; }

    /// <summary>CT-e do Siagro. FK Restrict: CT-e faturado não se exclui.</summary>
    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    public int LineNum { get; set; }
    [Column(TypeName = "DECIMAL(18,2)")] public decimal Amount { get; set; }

    /// <summary>Mantido pelos serviços: false quando a fatura é cancelada, liberando o CT-e para nova fatura.</summary>
    public bool IsActive { get; set; } = true;
}
