using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conciliação fiscal de compras (spec 2026-10-13 §3.1): quanto de uma linha de NF do fornecedor cobre um romaneio de
/// compra (8/9/10/16/17). N:N com quantidade em KG, sem efeito em saldo. Um par (linha, romaneio) só existe uma vez.
/// </summary>
[Table("PURCHASE_FISCAL_RECONCILIATIONS")]
public class PurchaseFiscalReconciliation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid PurchaseInvoiceItemKey { get; set; }
    public PurchaseInvoiceItem? PurchaseInvoiceItem { get; set; }

    public Guid StorageTransactionKey { get; set; }
    public StorageTransaction? StorageTransaction { get; set; }

    /// <summary>Quantidade fiscal atribuída ao romaneio, em KG. Sem teto pelo peso do romaneio (D7).</summary>
    [Column(TypeName = "DECIMAL(19,6)")]
    public decimal Quantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
