using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsConfirmedService(IUnitOfWork unitOfWork,ILogger<StorageTransactionsConfirmedService> logger)
{
    public async Task ExecuteAsync(
        Guid key,
        string userName, 
        CommitMode commitMode = CommitMode.Auto, 
        bool isShipmentTransaction = false)
    {
        var st = await unitOfWork.Context.StorageTransactions
                     .Include(x => x.QualityInspections)
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                        throw new NotFoundException("Storage transaction not found.");

        switch (st.TransactionType)
        {
            case StorageTransactionType.SalesShipment:
                await ExecuteSalesShipmentTransactionAsync(st, userName, commitMode, isShipmentTransaction);
                break;
            case StorageTransactionType.SalesShipmentReturn:
                await ExecuteSalesShipmentReturnTransactionAsync(st, userName, commitMode);
                break;
            default:
                await ExecutePurchaseTransactionAsync(st, userName, commitMode);
                break;
        }
    }
    
    public async Task ExecuteAsync(
        StorageTransaction st,
        string userName,
        CommitMode commitMode = CommitMode.Auto,
        bool isShipmentTransaction = false)
    {
        switch (st.TransactionType)
        {
            case StorageTransactionType.SalesShipment:
                await ExecuteSalesShipmentTransactionAsync(st, userName, commitMode, isShipmentTransaction);
                break;
            case StorageTransactionType.SalesShipmentReturn:
                await ExecuteSalesShipmentReturnTransactionAsync(st, userName, commitMode);
                break;
            default:
                await ExecutePurchaseTransactionAsync(st, userName, commitMode);
                break;
        }
    }
    
    private async Task ExecuteSalesShipmentTransactionAsync(
        StorageTransaction st, 
        string userName, 
        CommitMode commitMode = CommitMode.Auto,
        bool isShipmentTransaction = false)
    {
        if (st.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("Only pending transactions can be confirmed.");
        }
        
        var warehouseCode = st.WarehouseCode;
        var itemCode = st.ItemCode;
        
        var warehouseBalance = await GetWarehouseBalanceAsync(warehouseCode, itemCode);
        
        if (!isShipmentTransaction) 
            logger.LogInformation("Saldo no armazem: {}", warehouseBalance);
        
        if (!isShipmentTransaction && st.GrossWeight > warehouseBalance)
            throw new ApplicationException("A quantidade embarcada é superior ao saldo disponivel no armazem.");
            
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight;
            st.AvaiableVolumeToAllocate = decimal.Zero;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
    
    private async Task ExecuteSalesShipmentReturnTransactionAsync(StorageTransaction st, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (st.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("Only pending transactions can be confirmed.");
        }
        
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight;
            st.AvaiableVolumeToAllocate = decimal.Zero;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
    
    private async Task ExecutePurchaseTransactionAsync(
        StorageTransaction st, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (st.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("Only pending transactions can be confirmed.");
        }
        
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = CalculateNetWeight(st);
            st.AvaiableVolumeToAllocate = st.NetWeight;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
    
    private decimal CalculateNetWeight(StorageTransaction st)
    {
        return st.GrossWeight - (st.DryingDiscount + st.CleaningDiscount + st.OthersDicount);
    }
    
    private async Task<decimal> GetWarehouseBalanceAsync(string warehouseCode, string itemCode)
    {
        var total = await unitOfWork.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionStatus == StorageTransactionsStatus.Confirmed &&
                        x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        (x.TransactionType == StorageTransactionType.Purchase ||
                         x.TransactionType == StorageTransactionType.PurchaseReturn ||
                         x.TransactionType == StorageTransactionType.SalesShipment ||
                         x.TransactionType == StorageTransactionType.SalesShipmentReturn))
            .SumAsync(x => (x.TransactionType == StorageTransactionType.Purchase || 
                            x.TransactionType == StorageTransactionType.SalesShipmentReturn)
                ? x.NetWeight 
                : -x.NetWeight);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }
    
    //todo: finalizar metodo de calculo
    private async Task Calculate(StorageTransaction st)
    {
        var processingCostCode = st.ProcessingCostCode;
        var grossWeight = st.GrossWeight;

        var processingCost = await unitOfWork.Context.ProcessingCosts
                                 .Include(x => x.DryingDetails)
                                 .Include(x => x.DryingParameters)
                                 .Include(x => x.QualityParameters)
                                 .Include(x => x.ServiceDetails)
                                 .FirstOrDefaultAsync(x => x.Code == processingCostCode) ??
                             throw new NotFoundException("Processing Cost List not found.");

        var dryingInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Drying)
            .ToList();
        
        var cleaningInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Cleaning)
            .ToList();
        
        var qualityInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Quality)
            .ToList();

        foreach (var inspection in dryingInspection)
        {
            var moisture = inspection.Value;

            var dryingParameter = processingCost.DryingParameters
                .FirstOrDefault(x => x.InitialMoisture >= moisture && x.FinalMoisture <= moisture);

            var dryingDiscount = (grossWeight / 100) * dryingParameter?.Rate ?? 0;
            st.DryingDiscount = dryingDiscount;
            
            var dryingDetail = processingCost.DryingDetails
                .FirstOrDefault(x => x.InitialMoisture >= moisture && x.FinalMoisture <= moisture);

            var dryingServicePrice = dryingDiscount * dryingDetail?.Price ?? 0;
            
        }
        
        foreach (var inspection in cleaningInspection)
        {
            var value = inspection.Value;
            
            var qualityParameter = processingCost.QualityParameters
                .FirstOrDefault(x => value <= x.MaxLimitRate && x.QualityAttribCode == inspection.QualityAttribCode);

            var cleaningDiscount = (grossWeight / 100) * qualityParameter?.ExcessDiscountRate ?? 0;

            st.CleaningDiscount = cleaningDiscount;
        }
        
        foreach (var inspection in qualityInspection)
        {
            var value = inspection.Value;
            
            var qualityParameter = processingCost.QualityParameters
                .FirstOrDefault(x => value <= x.MaxLimitRate && x.QualityAttribCode == inspection.QualityAttribCode);

            var qualityDiscount = (grossWeight / 100) * qualityParameter?.ExcessDiscountRate ?? 0;

            st.OthersDicount = qualityDiscount;
        }
    }
}