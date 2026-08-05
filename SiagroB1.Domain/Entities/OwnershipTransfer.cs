using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("OWNERSHIP_TRANSFER")]
public class OwnershipTransfer : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50)")] 
    public string? TransferCode { get; set; } = string.Empty;
    
    public DateTime? Date { get; set; } = DateTime.Now.Date;
    
    public OwnershipTransferStatus? TransferStatus { get; set; } =  OwnershipTransferStatus.Open; 
    
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string?  ItemName { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressOriginCode { get; set; }
    public virtual StorageAddress? StorageAddressOrigin { get; set; }
    
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressDestinationCode { get; set; }
    public virtual StorageAddress? StorageAddressDestination { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UomCode { get; set; }


    /// <summary>
    /// Contrato de compra baixado por esta transferência. Opcional: só pode ser
    /// informado quando o lote de destino é estoque próprio
    /// (<see cref="StorageOwnershipType.OwnedInOurCustody"/>) e o de origem não é.
    /// </summary>
    /// <remarks>
    /// Confirmar a transferência com contrato emite uma
    /// <see cref="ShipmentRelease"/> já <c>Actived</c> e com saldo. O eixo de
    /// ALOCAÇÃO do contrato não é tocado aqui — quem aloca é o romaneio
    /// <c>Purchase(8)</c> criado depois pela Expedição de Grãos.
    /// </remarks>
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    /// <summary>Código do contrato, desnormalizado para exibição (igual a <see cref="ItemName"/>).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? PurchaseContractCode { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
}