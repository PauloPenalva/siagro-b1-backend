using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Recusa de carga registrada na filial que emite NF-e pelo Siagro (spec 2026-10-09). Guarda o destino escolhido
/// até a última NF-e de entrada ser autorizada — é a confirmação dessa devolução que aplica os efeitos.
/// </summary>
/// <remarks>
/// Uma recusa <see cref="ShipmentLoadRefusalStatus.Pending"/> por carga, no máximo (índice único filtrado em
/// <c>AppDbContext</c>). As devoluções apontam para cá por <see cref="SalesInvoice.ShipmentLoadRefusalKey"/>.
/// Não herda <c>BaseEntity</c>, como <see cref="ShipmentLoadTransshipment"/>: é registro filho da carga.
/// </remarks>
[Table("SHIPMENT_LOAD_REFUSALS")]
public class ShipmentLoadRefusal
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public RefusalDestination Destination { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? DestinationWarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? DestinationWarehouseName { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public required string Reason { get; set; }

    public ShipmentLoadRefusalStatus Status { get; set; } = ShipmentLoadRefusalStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? CompletedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CompletedBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CancelledBy { get; set; }
}
