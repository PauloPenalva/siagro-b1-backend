using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsAllocationCreateService(
    IUnitOfWork  unitOfWork,
    StorageTransactionsGetService storageTransactionsGetService)
{
    /// <summary>
    /// Carrega o contrato rastreado, SEM os includes de PurchaseContractsGetService.
    /// </summary>
    /// <remarks>
    /// Aquele Get inclui navegações de FK obrigatória (HarvestSeason, DocNumber), e o
    /// EF traduz FK obrigatória como INNER JOIN: faltando a linha mestre, a consulta
    /// devolve <c>null</c> e o contrato "some". Como o resto do método usa `?.`, o
    /// efeito era silencioso e grave — a alocação era gravada e o AllocatedVolume do
    /// contrato NÃO era atualizado, deixando o saldo físico defasado.
    ///
    /// Aqui só interessam Status e AvaiableVolume, ambos escalares — nenhuma navegação
    /// é necessária.
    /// </remarks>
    private async Task<PurchaseContract?> LoadContractAsync(Guid purchaseContractKey) =>
        await unitOfWork.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == purchaseContractKey);

    /// <summary>
    /// Entrada da tela/API: contrato de outro fornecedor ou outro Local de Entrega não se aloca
    /// direto — é a troca de fornecedor/armazém (<see cref="PurchaseTransactionsChangeSupplierService"/>).
    /// A trava fica só aqui: os fluxos internos (<see cref="ExecuteAsync"/>, estorno, descarga)
    /// alocam por regras próprias. A pernada de compra de uma Expedição segue regra própria: só o contrato da
    /// liberação (<see cref="ShippingPurchaseLegRules"/>), sem comparar fornecedor/armazém.
    /// </summary>
    private async Task EnsureSameSupplierAndWarehouseAsync(Guid purchaseContractKey, Guid storageTransactionKey)
    {
        var transaction = await unitOfWork.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.Key == storageTransactionKey)
            .Select(x => new { x.Key, x.CardCode, x.WarehouseCode })
            .FirstOrDefaultAsync();
        var contract = await unitOfWork.Context.PurchaseContracts
            .AsNoTracking()
            .Where(x => x.Key == purchaseContractKey)
            .Select(x => new { x.CardCode, x.DeliveryLocationCode })
            .FirstOrDefaultAsync();

        // Inexistente: o ExecuteAsync responde com o erro de sempre.
        if (transaction == null || contract == null)
            return;

        // Perna de compra de Expedição: só no contrato da liberação (e sem a trava de fornecedor/armazém,
        // já que o armazém dela é o da liberação). Mudar de contrato é a "Trocar liberação" da carga.
        var releaseContractKey = await ShippingPurchaseLegRules.ReleaseContractKeyAsync(
            unitOfWork.Context, transaction.Key);
        if (releaseContractKey != null)
        {
            if (releaseContractKey != purchaseContractKey)
            {
                var releaseCode = await unitOfWork.Context.PurchaseContracts
                    .AsNoTracking()
                    .Where(x => x.Key == releaseContractKey)
                    .Select(x => x.Code)
                    .FirstOrDefaultAsync();
                throw new ApplicationException(
                    $"Romaneio de Expedição só pode ser alocado no contrato da liberação ({releaseCode}). " +
                    "Para mudar o contrato, use Trocar liberação na carga.");
            }
            return;
        }

        if (!string.Equals(transaction.CardCode, contract.CardCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(transaction.WarehouseCode, contract.DeliveryLocationCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                "O contrato tem outro fornecedor ou armazém: use a troca de fornecedor/armazém.");
    }

    public async Task ExecuteWithTransactionAsync(Guid purchaseContractKey, Guid storageTransactionKey, decimal volume,
        string userName)
    {
        try
        {
            await EnsureSameSupplierAndWarehouseAsync(purchaseContractKey, storageTransactionKey);
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
        var purchaseContract = await LoadContractAsync(purchaseContractKey);

        if (purchaseContract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível alocar.");

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

            var changedQuantity = await PurchaseTransactionChangeQuery.ChangedQuantityAsync(
                unitOfWork.Context, storageTransaction.Key);

            storageTransaction.RecalculateAvailableVolume(existingAllocated + decimal.Abs(volume), changedQuantity);

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
        var purchaseContract = await LoadContractAsync(purchaseContractKey);

        if (purchaseContract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível alocar.");

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

        await ApplyAllocationAsync(purchaseContractKey, purchaseContract, storageTransaction, volume, userName, commitMode);
    }

    /// <summary>
    /// Estorno de troca de liberação (GAC-1177): aloca volume NEGATIVO ao romaneio 9
    /// (<see cref="StorageTransactionType.PurchaseReturn"/>) que representa a devolução física
    /// no contrato de ORIGEM. Ao contrário de <see cref="ExecuteAsync(Guid,StorageTransaction,decimal,string,CommitMode)"/>,
    /// este método DEVOLVE saldo em vez de consumi-lo, então nenhum dos dois guards que existem
    /// para impedir estourar saldo se aplica aqui:
    /// <list type="bullet">
    /// <item>contrato <see cref="ContractStatus.Finished"/> não bloqueia — devolver saldo a um
    /// contrato encerrado é seguro, é consumi-lo que não é (mesmo espírito de
    /// <c>PurchaseContractsCloseService</c>, que permite fechar com saldo negativo);</item>
    /// <item>o teto de <see cref="PurchaseContract.AvaiableVolume"/> do contrato não se aplica —
    /// devolver saldo não pode "estourar" saldo disponível, só aumentá-lo.</item>
    /// </list>
    /// O único teto que resta é o do PRÓPRIO romaneio de devolução: não dá para estornar mais do
    /// que ele ainda tem disponível para alocar (<see cref="StorageTransaction.AvaiableVolumeToAllocate"/>).
    /// </summary>
    public async Task ExecuteReversalAsync(
        Guid purchaseContractKey,
        StorageTransaction purchaseReturn,
        decimal volume,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (purchaseReturn.TransactionType != StorageTransactionType.PurchaseReturn)
        {
            throw new ApplicationException("Only purchase return transactions are supported.");
        }

        if (purchaseReturn.TransactionStatus == StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("The storage transaction is pending.");
        }

        if (volume <= 0)
        {
            throw new ApplicationException("Invalid purchase contract allocation volume.");
        }

        if (volume > purchaseReturn.AvaiableVolumeToAllocate)
        {
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the delivery note.");
        }

        var purchaseContract = await LoadContractAsync(purchaseContractKey);

        await ApplyAllocationAsync(purchaseContractKey, purchaseContract, purchaseReturn, -volume, userName, commitMode);
    }

    /// <summary>
    /// Troca nota: aloca o romaneio de quebra (<see cref="StorageTransactionType.PurchaseDischargeLoss"/>,
    /// volume NEGATIVO — devolve saldo ao contrato) ou de sobra
    /// (<see cref="StorageTransactionType.PurchaseDischargeGain"/>, volume positivo) gerado pela
    /// Conferência de Entregas. <paramref name="volume"/> chega sempre positivo.
    /// </summary>
    /// <remarks>
    /// Contrato encerrado recusa os DOIS sentidos (decisão do usuário, 19/09/2026: a conferência é
    /// recusada até o contrato ser reaberto) — ao contrário de <see cref="ExecuteReversalAsync"/>.
    /// O teto do saldo do contrato só vale para a sobra; a quebra devolve saldo.
    /// </remarks>
    public async Task ExecuteDischargeAsync(
        Guid purchaseContractKey,
        StorageTransaction discharge,
        decimal volume,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (discharge.TransactionType is not (StorageTransactionType.PurchaseDischargeLoss
                                           or StorageTransactionType.PurchaseDischargeGain))
            throw new ApplicationException("Only purchase discharge transactions are supported.");

        if (volume <= 0)
            throw new ApplicationException("Invalid purchase contract allocation volume.");

        if (volume > discharge.AvaiableVolumeToAllocate)
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the delivery note.");

        var purchaseContract = await LoadContractAsync(purchaseContractKey);

        if (purchaseContract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível alocar.");

        var isGain = discharge.TransactionType == StorageTransactionType.PurchaseDischargeGain;

        if (isGain && volume > purchaseContract?.AvaiableVolume)
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the purchase contract.");

        await ApplyAllocationAsync(
            purchaseContractKey, purchaseContract, discharge, isGain ? volume : -volume, userName, commitMode);
    }

    /// <summary>
    /// Trecho comum aos dois fluxos (alocação normal e estorno): grava a alocação, deriva o
    /// saldo alocável do romaneio a partir da soma persistida e atualiza o volume alocado do
    /// contrato a partir da soma COM sinal das alocações dele.
    /// </summary>
    private async Task ApplyAllocationAsync(
        Guid purchaseContractKey,
        PurchaseContract? purchaseContract,
        StorageTransaction storageTransaction,
        decimal volume,
        string userName,
        CommitMode commitMode)
    {
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

            // Romaneio ainda não persistido (deferred): não há troca registrada.
            var changedQuantity = storageTransaction.Key == Guid.Empty
                ? decimal.Zero
                : await PurchaseTransactionChangeQuery.ChangedQuantityAsync(
                    unitOfWork.Context, storageTransaction.Key);

            storageTransaction.RecalculateAvailableVolume(existingAllocated + decimal.Abs(volume), changedQuantity);

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