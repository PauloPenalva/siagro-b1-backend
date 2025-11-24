using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.StorageTransactions;

public class StorageTransactionsConfirmedService(AppDbContext db)
{
    public async Task ExecuteAsync(Guid key, StorageTransactionsConfirmedDto confirmedDto ,string userName)
    {
        var applyProcessingCost = false;
        
        var st = await db.StorageTransactions
                     .Include(x => x.QualityInspections)
                     .Where(x => x.TransactionStatus == StorageTransactionsStatus.Pending)
                     .FirstOrDefaultAsync() ??
                        throw new NotFoundException("Storage transaction not found.");

        if (st.TransactionType is StorageTransactionType.Receipt)
        {
            if (confirmedDto.ProcessingCostCode == null)
            {
                throw new ApplicationException("Processing cost list is required.");
            }
            
            applyProcessingCost =  true;
        }
        
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            st.ProcessingCostCode = confirmedDto.ProcessingCostCode;
            st.TransactionStatus = StorageTransactionsStatus.Confirmed;

            if (applyProcessingCost)
            {
                await Calculate(st);
            }
        
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }

    //todo: finalizar metodo de calculo
    private async Task Calculate(StorageTransaction st)
    {
        var processingCostKey = st.ProcessingCostCode;

        var processingCost = await db.ProcessingCosts
                .Include(x => x.DryingDetails)
                .Include(x => x.DryingParameters)
                .Include(x => x.QualityParameters)
                .Include(x => x.ServiceDetails)
                .FirstOrDefaultAsync(x => x.Code == processingCostKey) ??
                    throw new NotFoundException("Processing Cost List not found.");

        var dryingInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Drying)
            .ToList();
        
        var cleaningInspection = st.QualityInspections
            .Where(x => x.QualityAttrib?.Type == QualityAttribType.Cleaning)
            .ToList();

        foreach (var inspection in dryingInspection)
        {
            var moisture = inspection.Value;

            var dryingParameter = processingCost.DryingParameters
                .FirstOrDefault(x => x.InitialMoisture >= moisture && x.FinalMoisture <= moisture);
                
            var dryingDetail = processingCost.DryingDetails
                .FirstOrDefault(x => x.InitialMoisture >= moisture && x.FinalMoisture <= moisture);
        }
        
        foreach (var inspection in cleaningInspection)
        {
            var value = inspection.Value;
            
            var qualityParameter = processingCost.QualityParameters
                .FirstOrDefault(x => value <= x.MaxLimitRate);
        }
    }
}