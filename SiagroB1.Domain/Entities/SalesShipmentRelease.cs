using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Liberação de entrega/embarque de um contrato de VENDA. Entidade paralela ao
/// <see cref="ShipmentRelease"/> (que é exclusivo de compra) — mesmo ciclo de vida e
/// mesmas regras de saldo, porém vinculada a <see cref="SalesContract"/> e consumida por
/// romaneios de venda (<c>SalesShipment</c>) no faturamento.
/// </summary>
[Table("SALES_SHIPMENT_RELEASES")]
public class SalesShipmentRelease : DocumentEntity
{
    public required Guid SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    public DateTime ReleaseDate { get; set; } = DateTime.Now.Date;

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReleasedQuantity { get; set; } // Quantidade liberada

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string DeliveryLocationCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? DeliveryLocationName { get; set; }

    public ReleaseStatus Status { get; set; } = ReleaseStatus.Pending;

    /// <summary>
    /// Motivo informado no cancelamento da liberação (obrigatório na ação de cancelar).
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    public virtual ICollection<StorageTransaction> Transactions { get; } = [];

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ShippedQuantity { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion).
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Saldo disponível para romanear/faturar, derivado de <see cref="ShippedQuantity"/>
    /// (persistido, recalculado nos hooks de faturamento). Não depende de navegação.
    /// </summary>
    [NotMapped]
    public decimal AvailableQuantity =>
        Status != ReleaseStatus.Cancelled
            ? CalculateAvailableQuantity(ReleasedQuantity, ShippedQuantity)
            : decimal.Zero;

    /// <summary>
    /// Regra de arredondamento do saldo, compartilhada com quem precisa avaliá-lo
    /// antes de gravar (ex.: <c>SalesShipmentReleasesCancelationService</c>).
    /// </summary>
    public static decimal CalculateAvailableQuantity(decimal releasedQuantity, decimal shippedQuantity) =>
        decimal.Round(releasedQuantity - shippedQuantity, 3, MidpointRounding.ToEven);

    /// <summary>
    /// Quantidade que esta liberação consome do contrato de origem.
    /// Cancelada => apenas o que foi efetivamente romaneado/faturado (o saldo não romaneado
    /// volta para <c>SalesContract.TotalAvailableToRelease</c>); caso contrário, o total liberado.
    /// Derivada de <see cref="ShippedQuantity"/> (persistido) — não depende de navegação.
    /// </summary>
    [NotMapped]
    public decimal ConsumedQuantity =>
        Status == ReleaseStatus.Cancelled
            ? decimal.Round(Math.Max(decimal.Zero, ShippedQuantity), 3, MidpointRounding.ToEven)
            : ReleasedQuantity;

    /// <summary>
    /// Volume que o cancelamento devolveu ao contrato de origem — o saldo que estava
    /// liberado mas não chegou a ser romaneado/faturado. Zero enquanto a liberação não é
    /// cancelada. Vale sempre: <see cref="ConsumedQuantity"/> + este = <see cref="ReleasedQuantity"/>.
    /// </summary>
    [NotMapped]
    public decimal ReturnedToContractQuantity =>
        Status == ReleaseStatus.Cancelled
            ? decimal.Round(Math.Max(decimal.Zero, ReleasedQuantity - ConsumedQuantity), 3, MidpointRounding.ToEven)
            : decimal.Zero;
}
