using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento da troca de fornecedor/armazém de um romaneio de compra (tipo 8), total ou só do
/// saldo ainda não alocado: amarra o romaneio original, o estorno gerado (tipo 9) e o romaneio
/// novo que assume a quantidade trocada com o novo fornecedor/armazém.
/// </summary>
[Table("PURCHASE_TRANSACTION_CHANGES")]
[Index(nameof(OriginalStorageTransactionKey))]
public class PurchaseTransactionChange : BaseEntity
{
    /// <summary>Romaneio original, que perde a quantidade trocada.</summary>
    public Guid OriginalStorageTransactionKey { get; set; }
    public StorageTransaction? OriginalStorageTransaction { get; set; }

    /// <summary>Estorno (tipo 9) gerado na origem; nunca é alocável.</summary>
    public Guid ReturnStorageTransactionKey { get; set; }
    public StorageTransaction? ReturnStorageTransaction { get; set; }

    /// <summary>Romaneio novo, com o novo fornecedor/armazém.</summary>
    public Guid NewStorageTransactionKey { get; set; }
    public StorageTransaction? NewStorageTransaction { get; set; }

    /// <summary>Contrato em que o romaneio novo foi alocado.</summary>
    public Guid PurchaseContractKey { get; set; }

    public required string OriginalCardCode { get; set; }
    public required string OriginalWarehouseCode { get; set; }
    public required string NewCardCode { get; set; }
    public required string NewWarehouseCode { get; set; }

    /// <summary>Quantidade trocada (a que sai do saldo alocável do original).</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Quantity { get; set; }

    /// <summary>Volume alocado ao contrato no romaneio novo.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>Motivo da troca, digitado pelo operador.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    [MaxLength(500)]
    public required string Reason { get; set; }
}
