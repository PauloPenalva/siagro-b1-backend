using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um romaneio expedido coberto por uma NF-e de retorno do armazém (spec 2026-10-10 §2.4). Um retorno cobre N
/// romaneios; cada romaneio, inteiro, no máximo um retorno (índice único).
/// </summary>
[Table("STORAGE_RETURN_RECONCILIATIONS")]
public class StorageReturnReconciliation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid PurchaseInvoiceKey { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public Guid StorageTransactionKey { get; set; }
    public StorageTransaction? StorageTransaction { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
