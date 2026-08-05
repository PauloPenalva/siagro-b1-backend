using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.OwnershipTransfers.Factories;

/// <summary>
/// Monta a liberação de embarque emitida pela confirmação de uma transferência de
/// titularidade. Fonte única da regra "a liberação nasce liberada, com saldo, e
/// sabe em que lote o grão está".
/// </summary>
/// <remarks>
/// Não usa <c>ShipmentReleasesCreateService</c> de propósito: ele força
/// <c>Status = Pending</c> e chama <c>SaveChangesAsync</c> por conta própria, sem
/// parâmetro de <c>CommitMode</c> — o que quebraria a atomicidade da confirmação.
/// </remarks>
public static class OwnershipTransferShipmentReleaseFactory
{
    public static ShipmentRelease CreateFrom(
        OwnershipTransfer transfer,
        StorageAddress destination,
        PurchaseContract contract,
        string userName) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        BranchCode = contract.BranchCode,
        ReleaseDate = transfer.Date ?? DateTime.Now.Date,

        // Um contrato por transferência, consumindo a quantidade inteira.
        ReleasedQuantity = transfer.Quantity,
        ShippedQuantity = decimal.Zero,

        DeliveryLocationCode = destination.WarehouseCode,
        DeliveryLocationName = destination.WarehouseName,

        // O lote é o que permite a Expedição de Grãos drenar o estoque próprio
        // criado pela transferência, em vez de deixá-lo como saldo fantasma.
        StorageAddressCode = destination.Code,

        // Nasce liberada e COM saldo: o físico já está em nosso poder, mas a
        // mercadoria ainda precisa ser embarcada para o faturamento. Encerrá-la aqui
        // a tiraria da Expedição de Grãos.
        Status = ReleaseStatus.Actived,
        ApprovedAt = DateTime.Now,
        ApprovedBy = userName,

        Origin = ReleaseOrigin.OwnershipTransfer,
        OwnershipTransferKey = transfer.Key,

        CreatedAt = DateTime.Now,
        CreatedBy = userName,
    };
}
