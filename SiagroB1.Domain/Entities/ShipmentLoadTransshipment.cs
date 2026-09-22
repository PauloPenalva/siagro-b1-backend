using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um transbordo da carga (GAC-1181): a mercadoria é descarregada num armazém intermediário e
/// recarregada dali para o cliente, sem que a viagem vire duas cargas.
/// </summary>
/// <remarks>
/// <b><see cref="OutgoingQuantity"/> é o saldo disponível INTEIRO da carga no momento</b>, porque
/// tudo é descarregado — não existe descarregar parte e seguir com o resto no caminhão. É ele o
/// quarto termo do saldo da carga.
/// <para>
/// <see cref="EntryQuantity"/> é o peso PESADO na entrada e pode ser menor: a diferença
/// (<see cref="ShrinkageQuantity"/>) é quebra de transporte, meramente informativa — não volta ao
/// contrato e ninguém a fatura.
/// </para>
/// <para>
/// Não herda <c>BaseEntity</c>, como <see cref="ShipmentLoadDischarge"/>: é registro filho de
/// documento, não documento.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_TRANSSHIPMENTS")]
[Index(nameof(ShipmentLoadKey))]
[Index(nameof(EntryStorageTransactionKey))]
[Index(nameof(LotExitStorageTransactionKey))]
public class ShipmentLoadTransshipment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    /// <summary>1, 2, 3… dentro da carga. É por ele que se acha o ÚLTIMO transbordo.</summary>
    public int Sequence { get; set; }

    public TransshipmentOrigin Origin { get; set; } = TransshipmentOrigin.Planned;

    [Column(TypeName = "VARCHAR(10)")]
    public required string WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    public DateTime TransshipmentDate { get; set; } = DateTime.Now.Date;

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal OutgoingQuantity { get; set; }

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal EntryQuantity { get; set; }

    /// <summary>
    /// Romaneio de entrada: o <see cref="StorageTransactionType.TransshipmentReceipt"/> em armazém
    /// de terceiro, ou o <see cref="StorageTransactionType.Receipt"/> da Entrada em Armazenagem em
    /// armazém próprio. Nulo enquanto a entrada não foi registrada.
    /// </summary>
    public Guid? EntryStorageTransactionKey { get; set; }
    public virtual StorageTransaction? EntryStorageTransaction { get; set; }

    /// <summary>
    /// Romaneio <see cref="StorageTransactionType.Shipment"/> (1) que o armazém pesou ao recarregar
    /// o grão de volta para o cliente, em armazém PRÓPRIO — a saída do LOTE. Vinculá-lo (GAC-1181
    /// fase 2, Task 4) é o que EMITE a liberação, pela quantidade REAL carregada. Nulo enquanto a
    /// saída não foi vinculada.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Vinculá-lo não conclui o transbordo.</b> Quem fecha de verdade — e libera o
    /// faturamento — é a Expedição de venda (<see cref="StorageTransactionType.SalesShipment"/>, o
    /// 7) vinculada depois pelo caminho de sempre (<c>ShipmentLoadsAttachTransactionsService</c>):
    /// <c>ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync</c> só enxerga o
    /// tipo 7.
    /// </remarks>
    public Guid? LotExitStorageTransactionKey { get; set; }
    public virtual StorageTransaction? LotExitStorageTransaction { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    /// <summary>Quebra de transporte: o que saiu da carga menos o que entrou no armazém.</summary>
    [NotMapped]
    public decimal ShrinkageQuantity => EntryQuantity <= decimal.Zero
        ? decimal.Zero
        : decimal.Round(OutgoingQuantity - EntryQuantity, 3, MidpointRounding.ToEven);
}
