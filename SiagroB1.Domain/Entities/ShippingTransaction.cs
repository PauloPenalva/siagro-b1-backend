using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SHIPPING_TRANSACTIONS")]
public class ShippingTransaction : BaseEntity
{
    /// <summary>
    /// Perna comercial de COMPRA do embarque. Anulável porque o embarque de uma liberação
    /// emitida por transferência de titularidade não tem compra a registrar — ela já foi
    /// registrada e alocada no confirm da transferência, e a Expedição cria apenas a perna
    /// de saída. Ver <c>ShippingTransactionsCreateService</c>.
    /// </summary>
    public Guid? PurchaseStorageTransactionKey { get; set; }
    public virtual StorageTransaction? PurchaseStorageTransaction { get; set; }

    public Guid SalesStorageTransactionKey { get; set; }
    public virtual StorageTransaction? SalesStorageTransaction { get; set; }
}
