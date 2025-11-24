using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesApprovationService(AppDbContext context, ILogger<ShipmentReleasesApprovationService> logger)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var sr = await context.ShipmentReleases
                     .Include(x => x.PurchaseContract)
                     .ThenInclude(x => x.DeliveryLocation)
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");
        
        if (sr.Status != ReleaseStatus.Pending)
        {
            throw new ArgumentException("Shipment Release not pending.");
        }

        if (sr.PurchaseContract == null)
        { 
            throw new NotFoundException($"Purchase Contract not found key {sr.PurchaseContractKey}");
        }
        
        if (sr.PurchaseContract.Status != ContractStatus.Approved)
        {
            // contrato de compra não está aprovado.
            throw new ArgumentException("Purchase Contract not approved.");
        }
       
        // se a quantidade liberada é superior ao total disponivel do contrato
        // lança a exceção
        if (sr.ReleasedQuantity > sr.PurchaseContract.TotalAvailableToRelease)
        {
            throw new ArgumentException("Quantity is higher than the total available to release.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            sr.Status = ReleaseStatus.Approved;
            sr.ExpectedDeliveryDate = sr.PurchaseContract.DeliveryEndDate;
            sr.ApprovedBy = userName;
            sr.ApprovedAt = DateTime.Now;

            await context.SaveChangesAsync();
            await CreateStorageTransaction(sr);  // cria entrada no lote de armazenamento.
    
            await transaction.CommitAsync();
        }
        catch(Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private async Task CreateStorageTransaction(ShipmentRelease sr)
    {
        var storageAddress = await context.StorageAddresses
            .FirstOrDefaultAsync(x => x.PurchaseContractKey == sr.PurchaseContractKey) ?? await CreateStorageAddress(sr);

        if (storageAddress.Key == null) throw new ApplicationException("Storage address not found.");
        
        storageAddress.Transactions.Add(new StorageTransaction
        {
            StorageAddressKey = (Guid) storageAddress.Key,
            TransactionType = StorageTransactionType.ShipmentReleased,
            GrossWeight = sr.ReleasedQuantity,
            ShipmentReleaseKey = sr.Key,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.ShipmentRelease
        });
        
        await context.SaveChangesAsync();
    }

    private async Task<StorageAddress> CreateStorageAddress(ShipmentRelease sr)
    {
        var storageAddress = new StorageAddress
        {
            BranchCode = sr.BranchCode,
            Code = sr.PurchaseContract.Code, //todo: buscar codigo
            PurchaseContractKey = sr.PurchaseContract.Key,
            CreationDate = DateTime.Now,
            OwnershipType = sr.PurchaseContract.DeliveryLocation.Type is WarehouseType.ThirdParty 
                ? StorageOwnershipType.OwnedInThirdPartyCustody
                : StorageOwnershipType.OwnedInOurCustody,
            Description = $"CONTRATO {sr.PurchaseContract.Code}/{sr.PurchaseContract.Sequence} PRODUTO: {sr.PurchaseContract.ItemCode} ", 
            CardCode = sr.PurchaseContract.CardCode,
            ItemCode = sr.PurchaseContract.ItemCode,
            WarehouseCode = sr.PurchaseContract.DeliveryLocationCode,
            TransactionOrigin = TransactionCode.ShipmentRelease
        };
        
        context.StorageAddresses.Add(storageAddress);
        await context.SaveChangesAsync();
        
        return storageAddress;
    }
}