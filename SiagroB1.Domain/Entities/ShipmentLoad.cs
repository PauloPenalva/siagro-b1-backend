using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Carga montada: agrupa romaneios de embarque (<c>SalesShipment</c>) do mesmo veículo,
/// produto e filial num documento próprio, com numeração, saldo a faturar e histórico.
/// É o pivô do fluxo de saída — <c>1:N romaneios → 1 ShipmentLoad ← 1:N documentos de saída</c>.
/// A nota não conhece mais romaneio: chega nele pela carga.
/// </summary>
/// <remarks>
/// <see cref="TotalQuantity"/> soma <c>GrossWeight</c> dos romaneios, e não <c>NetWeight</c>,
/// por continuidade estrita com o faturamento atual — é o bruto que hoje vira a quantidade
/// da nota. Trocar mudaria silenciosamente o volume faturado de toda a operação.
/// </remarks>
[Table("SHIPMENT_LOADS")]
[Index(nameof(Code), IsUnique = true)]
public class ShipmentLoad : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }

    public DateTime LoadDate { get; set; } = DateTime.Now.Date;

    public ShipmentLoadStatus Status { get; set; } = ShipmentLoadStatus.Open;

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; }

    [Column(TypeName = "VARCHAR(11) NULL")]
    public string? TruckDriverCode { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    /// <summary>
    /// Persistido-derivado: soma do <c>GrossWeight</c> dos romaneios da carga, gravada na montagem.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalQuantity { get; set; }

    /// <summary>
    /// Persistido-derivado: soma das notas VIVAS da carga (pendente e confirmada contam;
    /// cancelada não; devolução CONFIRMADA abate a origem). Escritor único:
    /// <c>ShipmentLoadsRecalculateInvoicedService</c>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal InvoicedQuantity { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    /// <summary>
    /// Motivo informado no cancelamento da carga (obrigatório na ação de cancelar).
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). É a ÚNICA proteção real contra
    /// duas notas parciais simultâneas: ambas passam pelos guards e só aqui a segunda falha.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public virtual ICollection<StorageTransaction> Transactions { get; } = [];

    public virtual ICollection<SalesInvoice> Invoices { get; } = [];

    public virtual ICollection<ShipmentLoadMovement> Movements { get; } = [];

    /// <summary>
    /// Saldo a faturar, derivado dos dois persistidos. Carga cancelada não tem saldo.
    /// </summary>
    [NotMapped]
    public decimal AvailableQuantity =>
        Status != ShipmentLoadStatus.Cancelled
            ? CalculateAvailableQuantity(TotalQuantity, InvoicedQuantity)
            : decimal.Zero;

    /// <summary>
    /// Regra de arredondamento do saldo, compartilhada com quem precisa avaliá-lo antes de
    /// gravar (ex.: <c>ShipmentLoadsBillingGuardService</c>).
    /// </summary>
    public static decimal CalculateAvailableQuantity(decimal totalQuantity, decimal invoicedQuantity) =>
        decimal.Round(totalQuantity - invoicedQuantity, 3, MidpointRounding.ToEven);

    [NotMapped]
    public bool IsFullyInvoiced => AvailableQuantity <= decimal.Zero;
}
