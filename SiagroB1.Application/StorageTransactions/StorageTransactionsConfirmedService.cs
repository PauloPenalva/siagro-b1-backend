using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsConfirmedService(AppDbContext db, ILogger<StorageTransactionsConfirmedService> logger)
{
    public async Task ExecuteAsync(Guid key,string userName)
    {
        var st = await db.StorageTransactions
                     .Include(x => x.QualityInspections)
                     .Where(x => x.TransactionStatus == StorageTransactionsStatus.Pending)
                     .FirstOrDefaultAsync() ??
                        throw new NotFoundException("Storage transaction not found.");

        switch (st.TransactionType)
        {
            case StorageTransactionType.SalesShipment:
                await ExecuteSalesShipementTransactionAsync(st, userName);
                break;
            case StorageTransactionType.SalesShipmentReturn:
                await ExecuteSalesShipementReturnTransactionAsync(st, userName);
                break;
            default:
                await ExecutePurchaseTransactionAsync(st, userName);
                break;
        }
    }
    
    private async Task ExecuteSalesShipementTransactionAsync(StorageTransaction st, string userName)
    {
        var warehouseCode = st.WarehouseCode;
        var itemCode = st.ItemCode;
        
        var warehouseBalance = await GetWarehouseBalanceAsync(warehouseCode, itemCode);
        logger.LogInformation("Saldo no armazem: {}", warehouseBalance);
        
        if (st.GrossWeight > warehouseBalance)
        {
            throw new ApplicationException("A quantidade embarcada é superior ao saldo disponivel no armazem.");
        }
            
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight;
            st.AvaiableVolumeToAllocate = decimal.Zero;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
    
    private async Task ExecuteSalesShipementReturnTransactionAsync(StorageTransaction st, string userName)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight;
            st.AvaiableVolumeToAllocate = decimal.Zero;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
    
    private async Task ExecutePurchaseTransactionAsync(StorageTransaction st, string userName)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // if (applyProcessingCost)
            // {
            //     await Calculate(st);
            // }
            
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight - (st.DryingDiscount + st.CleaningDiscount + st.OthersDicount);
            st.AvaiableVolumeToAllocate = st.NetWeight;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
    
    private async Task<decimal> GetWarehouseBalanceAsync(string warehouseCode, string itemCode)
    {
        var total = await db.StorageTransactions
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

        var processingCost = await db.ProcessingCosts
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