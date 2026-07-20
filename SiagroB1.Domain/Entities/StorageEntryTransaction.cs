using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Entrada de compra em armazenagem própria: liga o romaneio de compra (que baixa
/// contrato e liberação) ao romaneio de recebimento (que alimenta o lote).
///
/// Existe como entidade própria — e não como mais uma linha em SHIPPING_TRANSACTIONS —
/// para dar um agregado com estado próprio, que possa ser estornado como unidade.
/// </summary>
[Table("STORAGE_ENTRY_TRANSACTIONS")]
public class StorageEntryTransaction : BaseEntity
{
    public Guid PurchaseStorageTransactionKey { get; set; }
    public virtual StorageTransaction? PurchaseStorageTransaction { get; set; }

    public Guid ReceiptStorageTransactionKey { get; set; }
    public virtual StorageTransaction? ReceiptStorageTransaction { get; set; }

    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    [ForeignKey(nameof(StorageAddress))]
    public string? StorageAddressCode { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }

    public StorageEntryTransactionStatus Status { get; set; } = StorageEntryTransactionStatus.Confirmed;

    /// <summary>Volume alocado no contrato de compra (peso líquido do romaneio de compra).</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>
    /// Peso líquido que efetivamente entrou no lote. Pode divergir de
    /// <see cref="AllocatedVolume"/>: o Receipt recalcula os descontos usando o
    /// ProcessingCost do lote, que não é necessariamente o do romaneio de compra.
    /// Os dois ficam gravados para permitir reconciliação.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ReceiptNetWeight { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
