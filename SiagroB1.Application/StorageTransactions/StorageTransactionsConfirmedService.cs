using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsConfirmedService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource,
    ILogger<StorageTransactionsConfirmedService> logger
    )
{
    public async Task ExecuteAsync(
        Guid key,
        string userName, 
        CommitMode commitMode = CommitMode.Auto, 
        bool isShipmentTransaction = false)
    {
        var st = await db.Context.StorageTransactions
                     .Include(x => x.QualityInspections)
                     .ThenInclude(q => q.QualityAttrib)
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                        throw new NotFoundException("Storage transaction not found.");

        await Processing(st, userName, commitMode, isShipmentTransaction);
    }
    
    public async Task ExecuteAsync(
        StorageTransaction st,
        string userName,
        CommitMode commitMode = CommitMode.Auto,
        bool isShipmentTransaction = false)
    {
        await Processing(st, userName, commitMode, isShipmentTransaction);
    }

    private async Task Processing( 
        StorageTransaction st, 
        string userName, 
        CommitMode commitMode = CommitMode.Auto,
        bool isShipmentTransaction = false)
    {
        switch (st.TransactionType)
        {
            case StorageTransactionType.Receipt:
                await ExecuteReceiptTransactionAsync(st, userName, commitMode);
                break;
            case StorageTransactionType.Shipment:
                await ExecuteShipmentTransactionAsync(st, userName, commitMode);
                break;
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
            //Apenas romaneios pendentes podem ser confirmados.
            throw new ApplicationException(resource["EXCEPTION_00005"]); 
        }
        
        var warehouseCode = st.WarehouseCode;
        var itemCode = st.ItemCode;
        
        var warehouseBalance = await GetWarehouseBalanceAsync(warehouseCode, itemCode);
        
        if (!isShipmentTransaction) 
            logger.LogInformation("Saldo no armazem: {}", warehouseBalance);
        
        if (!isShipmentTransaction && st.GrossWeight > warehouseBalance)
            //A quantidade embarcada é superior ao saldo disponivel no armazem.
            throw new ApplicationException(resource["EXCEPTION_00006"]);
            
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight;
            st.AvaiableVolumeToAllocate = decimal.Zero;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
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
            //Apenas romaneios pendentes podem ser confirmados.
            throw new ApplicationException(resource["EXCEPTION_00005"]); 
        }
        
        try
        {
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = st.GrossWeight;
            st.AvaiableVolumeToAllocate = decimal.Zero;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
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
            //Apenas romaneios pendentes podem ser confirmados.
            throw new ApplicationException(resource["EXCEPTION_00005"]); 
        }
        
        try
        {
            await CalculateReceipt(st);
            
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = CalculateNetWeight(st);
            st.AvaiableVolumeToAllocate = st.NetWeight;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
    
    private async Task ExecuteReceiptTransactionAsync(
        StorageTransaction st, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (st.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            //Apenas romaneios pendentes podem ser confirmados.
            throw new ApplicationException(resource["EXCEPTION_00005"]); 
        }
        
        try
        {
            // calcula descontos e custos se for entrada ou compra
            await CalculateReceipt(st);
            
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = CalculateNetWeight(st);
            st.AvaiableVolumeToAllocate = st.NetWeight;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
    
    
    private async Task ExecuteShipmentTransactionAsync(
        StorageTransaction st, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (st.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            //Apenas romaneios pendentes podem ser confirmados.
            throw new ApplicationException(resource["EXCEPTION_00005"]); 
        }
        
        try
        {
            await CalculateShipment(st); 
                    
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;
            st.NetWeight = CalculateNetWeight(st);
            st.AvaiableVolumeToAllocate = st.NetWeight;
            st.UpdatedBy = userName;
            st.UpdatedAt = DateTime.Now;
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
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
        var total = await db.Context.StorageTransactions
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
    
    private async Task CalculateReceipt(StorageTransaction st)
    {
        var processingCostCode = st.ProcessingCostCode;
        var grossWeight = st.GrossWeight;
        var cleaningDiscount = decimal.Zero;
        var dryingDiscount = decimal.Zero;
        var dryingServicePrice = decimal.Zero;
        var qualityDiscount = decimal.Zero;

        var processingCost = await db.Context.ProcessingCosts
                .Include(x => x.DryingDetails)
                .Include(x => x.DryingParameters)
                .Include(x => x.QualityParameters)
                .Include(x => x.ServiceDetails)
                .FirstOrDefaultAsync(x => x.Code == processingCostCode)
            //?? throw new ApplicationException(resource["EXCEPTION_00004"]);
            ;
        
        if (processingCost == null)
            return;
        
        var dryingInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Drying)
            .ToList();
        
        var cleaningInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Cleaning)
            .ToList();
        
        var qualityInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Quality)
            .ToList();

        foreach (var inspection in cleaningInspection)
        {
            var value = inspection.Value;

            var qualityParameters = processingCost.QualityParameters
                .Where(x => x.QualityAttribCode == inspection.QualityAttribCode)
                .ToList();
            
            foreach (var parameter in qualityParameters)
            {
                var realValue = value - parameter.MaxLimitRate;
                if (realValue > 0)
                {
                    cleaningDiscount += (grossWeight / 100) * realValue ?? 0;    
                }
            }
            
        }
        
        st.CleaningDiscount = cleaningDiscount;
        st.CleaningServicePrice = cleaningDiscount > 0 
            ? grossWeight * processingCost?.CleaningPrice ?? 0 
            : 0;
        
        foreach (var inspection in dryingInspection)
        {
            var moisture = inspection.Value; // umidade %

            var dryingParameter = processingCost.DryingParameters
                .FirstOrDefault(x => x.InitialMoisture <= moisture && x.FinalMoisture >= moisture);

            dryingDiscount += 
                (processingCost.DryingCalculationMethod == DryingCalculationMethod.BeforeCleaning 
                    ? (grossWeight / 100)
                    : ((grossWeight - cleaningDiscount) / 100)
                ) * dryingParameter?.Rate ?? 0;
            
            var dryingDetail = processingCost.DryingDetails
                .FirstOrDefault(x => x.InitialMoisture <= moisture && x.FinalMoisture >= moisture);

            dryingServicePrice += grossWeight * dryingDetail?.Price ?? 0;
            
        }
        st.DryingDiscount = dryingDiscount;
        st.DryingServicePrice =  dryingServicePrice;
        
        foreach (var inspection in qualityInspection)
        {
            var value = inspection.Value;
            
            var qualityParameters = processingCost.QualityParameters
                .Where(x => x.QualityAttribCode == inspection.QualityAttribCode)
                .ToList();
            
            foreach (var parameter in qualityParameters)
            {
                var realValue = value - parameter.MaxLimitRate;
                if (realValue > 0)
                {
                    qualityDiscount += (grossWeight / 100) * realValue ?? 0;
                }
            }
        }
        st.OthersDicount = qualityDiscount;
        st.ReceiptServicePrice = grossWeight * processingCost?.ReceiptPrice ?? 0;
    }
    
    private async Task CalculateShipment(StorageTransaction st)
    {
        var processingCostCode = st.ProcessingCostCode;
        var grossWeight = st.GrossWeight;

        var processingCost = await db.Context.ProcessingCosts
            .FirstOrDefaultAsync(x => x.Code == processingCostCode);
    
        
        if (processingCost == null)
            return;
        
        st.ShipmentPrice = grossWeight * processingCost?.ShipmentPrice ?? 0;
    }
}