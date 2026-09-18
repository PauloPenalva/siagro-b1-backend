using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ticket de descarga da carga (GAC-1171): o que a balança do DESTINO pesou, segundo o documento
/// que o transportador entregou.
/// </summary>
/// <remarks>
/// Serve a duas coisas: liberar o pagamento do frete e confrontar o relatório de descarga do
/// cliente/trading. As duas fontes podem estar erradas por motivos diferentes — o ticket pode ter
/// sido adulterado pelo transportador, e o relatório pode ter sido alimentado com informação
/// errada.
/// <para>
/// ⚠️ Por isso o ticket NÃO é fonte da conferência. Ele soma em
/// <see cref="SalesInvoiceItem.TicketDeliveredQuantity"/> e nunca toca
/// <see cref="SalesInvoiceItem.DeliveredQuantity"/>, que é digitado pelo conferente a partir do
/// relatório da trading. Fazer o ticket preencher o peso conferido transformaria o número
/// possivelmente adulterado no padrão da conferência.
/// </para>
/// <para>
/// Guarda a nota E a linha da nota. A linha é o alvo do peso; a nota existe para o grid chegar ao
/// número dela com um <c>$expand</c> de um nível. O par é validado no servidor, porque nada impede
/// a tela de mandar uma combinação inconsistente.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_DISCHARGES")]
[Index(nameof(ShipmentLoadKey))]
[Index(nameof(SalesInvoiceItemKey))]
public class ShipmentLoadDischarge
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    public Guid? SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string TicketNumber { get; set; }

    public DateTime DischargeDate { get; set; } = DateTime.Now.Date;

    /// <summary>Peso do ticket. Mesma escala de <c>StorageTransaction.GrossWeight</c>.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal DischargedQuantity { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    /// <summary>
    /// Arquivo do ticket, quando anexado no próprio diálogo de registro. Anulável: o ticket pode
    /// ser lançado antes de o arquivo chegar.
    /// </summary>
    public Guid? AttachmentKey { get; set; }
    public virtual ShipmentLoadAttachment? Attachment { get; set; }

    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
