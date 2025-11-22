using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

// este registro só pode ser excluido ou alterdo se o status for Planned (Planejamento)
[Table("SHIPPING_ORDERS")]
public class ShippingOrder : BaseEntity
{
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }

    public DateTime? Date { get; set; } = DateTime.Now;

    //Quando mudar o status para Weighed (Pesado), criar o romaneio de saida (StorageTransaction)
    //Referente ao lote de estoque informado (StorageAddress)
    public ShippingOrderStatus? Status { get; set; } = ShippingOrderStatus.Planned;
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string TruckCode { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    public required string TruckDriver { get; set; }
    
    public required Guid StorageAddressKey { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Volume { get; set; }
    
    public string? Comments { get; set; }
}