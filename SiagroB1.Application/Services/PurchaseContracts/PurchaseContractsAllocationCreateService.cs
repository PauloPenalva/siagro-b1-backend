using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsAllocationCreateService(
    IUnitOfWork  unitOfWork,
    StorageTransactionsGetService storageTransactionsGetService,
    PurchaseContractsGetService purchaseContractsGetService)
{
    public async Task ExecuteWithTransactionAsync(Guid purchaseContractKey, Guid storageTransactionKey, decimal volume,
        string userName)
    {
        try
        {
            await unitOfWork.BeginTransactionAsync();
            await ExecuteAsync(purchaseContractKey, storageTransactionKey, volume, userName);
            await unitOfWork.CommitAsync();
        }
        catch (Exception e)
        {
            await unitOfWork.RollbackAsync();
            throw new DefaultException(e.Message);
        }
    }
    
    public async Task ExecuteAsync(
        Guid purchaseContractKey, 
        Guid storageTransactionKey, 
        decimal volume, 
        string userName, 
        CommitMode commitMode = CommitMode.Auto
        )
    {
        var storageTransaction = await storageTransactionsGetService.GetByIdAsync(storageTransactionKey);
        var purchaseContract = await purchaseContractsGetService.GetByIdAsync(purchaseContractKey);
        
        var allowedTypes = new[]
        {
            StorageTransactionType.Purchase,
            StorageTransactionType.PurchaseReturn,
            StorageTransactionType.PurchaseQtyComplement,
            StorageTransactionType.PurchasePriceComplement
        };

        if (!allowedTypes.Contains(storageTransaction.TransactionType))
        {
            throw new ApplicationException("Only purchase transaction are supported.");
        }

        if (storageTransaction.TransactionStatus == StorageTransactionsStatus.Pending)
        {
            throw  new ApplicationException("The storage transaction is pending.");
        }
        
        if (volume <= 0)
        {
            throw new ApplicationException("Invalid purchase contract allocation volume.");
        }

        if (volume > storageTransaction.AvaiableVolumeToAllocate)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the delivery note.");
        }

        if (volume > purchaseContract?.AvaiableVolume)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the purchase contract.");
        }

        volume = storageTransaction.TransactionType switch
        {
            StorageTransactionType.PurchaseReturn => 0 - volume,
            StorageTransactionType.PurchaseQtyComplement => 0,
            StorageTransactionType.PurchasePriceComplement => 0,
            _ => volume
        };

        try
        {
            var alloc = new PurchaseContractAllocation
            {
                PurchaseContractKey = purchaseContractKey,
                StorageTransactionKey = storageTransactionKey,
                Volume = volume,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            };

            await unitOfWork.Context.PurchaseContractsAllocations.AddAsync(alloc);

            var existingAllocated = await unitOfWork.Context.PurchaseContractsAllocations
                .Where(x => x.StorageTransactionKey == storageTransactionKey)
                .SumAsync(x => decimal.Abs(x.Volume));

            storageTransaction.RecalculateAvailableVolume(existingAllocated + decimal.Abs(volume));

            // Saldo do CONTRATO usa Volume com sinal (devolução negativa devolve saldo).
            var contractAllocated = await unitOfWork.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == purchaseContractKey)
                .SumAsync(x => x.Volume);

            if (purchaseContract != null)
                purchaseContract.AllocatedVolume = contractAllocated + volume;

            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();

        }
        catch (Exception e)
        {
            throw new DefaultException(e.Message);
        }
    }

    public async Task ExecuteAsync(
        Guid purchaseContractKey,
        StorageTransaction storageTransaction,
        decimal volume, 
        string userName, 
        CommitMode commitMode = CommitMode.Auto
        )
    {
        var purchaseContract = await purchaseContractsGetService.GetByIdAsync(purchaseContractKey);
        
        var allowedTypes = new[]
        {
            StorageTransactionType.Purchase,
            StorageTransactionType.PurchaseReturn,
            StorageTransactionType.PurchaseQtyComplement,
            StorageTransactionType.PurchasePriceComplement
        };

        if (!allowedTypes.Contains(storageTransaction.TransactionType))
        {
            throw new ApplicationException("Only purchase transaction are supported.");
        }

        if (storageTransaction.TransactionStatus == StorageTransactionsStatus.Pending)
        {
            throw  new ApplicationException("The storage transaction is pending.");
        }
        
        if (volume <= 0)
        {
            throw new ApplicationException("Invalid purchase contract allocation volume.");
        }

        if (volume > storageTransaction.AvaiableVolumeToAllocate)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the delivery note.");
        }

        if (volume > purchaseContract?.AvaiableVolume)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the purchase contract.");
        }

        volume = storageTransaction.TransactionType switch
        {
            StorageTransactionType.PurchaseReturn => 0 - volume,
            StorageTransactionType.PurchaseQtyComplement => 0,
            StorageTransactionType.PurchasePriceComplement => 0,
            _ => volume
        };

        try
        {
            var alloc = new PurchaseContractAllocation
            {
                PurchaseContractKey = purchaseContractKey,
                StorageTransaction = storageTransaction,
                Volume = volume,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            };

            await unitOfWork.Context.PurchaseContractsAllocations.AddAsync(alloc);

            // Romaneio pode ainda não estar persistido (fluxo de embarque, deferred):
            // nesse caso não há alocações no banco e o total prévio é zero.
            var existingAllocated = storageTransaction.Key == Guid.Empty
                ? decimal.Zero
                : await unitOfWork.Context.PurchaseContractsAllocations
                    .Where(x => x.StorageTransactionKey == storageTransaction.Key)
                    .SumAsync(x => decimal.Abs(x.Volume));

            storageTransaction.RecalculateAvailableVolume(existingAllocated + decimal.Abs(volume));

            // Saldo do CONTRATO usa Volume com sinal (devolução negativa devolve saldo).
            var contractAllocated = await unitOfWork.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == purchaseContractKey)
                .SumAsync(x => x.Volume);

            if (purchaseContract != null)
                purchaseContract.AllocatedVolume = contractAllocated + volume;

            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();

        }
        catch (Exception e)
        {
            throw new DefaultException(e.Message);
        }
    }
}