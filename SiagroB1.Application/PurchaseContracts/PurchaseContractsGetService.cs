using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsGetService(AppDbContext context, ILogger<PurchaseContractsUpdateService> logger)
{
    public async Task<PurchaseContract?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await context.PurchaseContracts
                .Include(p => p.PriceFixations)
                .Include(p => p.QualityParameters)
                .ThenInclude(qa => qa.QualityAttrib)
                .Include(p => p.Taxes)
                .ThenInclude(t => t.Tax)
                .Include(x => x.ShipmentReleases)
                .Include(x => x.DocType)
                .Include(x => x.Brokers)
                .FirstOrDefaultAsync(p => p.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<PurchaseContract> QueryAll()
    {
        return context.PurchaseContracts.AsNoTracking();
    }

    public List<PurchaseContractDto> GetAvaiablesPurchaseContracts(string cardCode, string itemCode)
    {
        var responseList = new List<PurchaseContractDto>();
        
        var contractsList = context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.Allocations)
            .Where(p => p.CardCode == cardCode && p.ItemCode == itemCode && p.Status == ContractStatus.Approved)
            .OrderBy(x => x.RowId)
            .ToList();
        
        contractsList.ForEach(x =>
        { 
            if (x.AvaiableVolume > 0)
            {
                responseList.Add(new PurchaseContractDto
                {
                    Key = x.Key,
                    Code = x.Code,
                    AvaiableVolume = x.AvaiableVolume,
                    UnitOfMeasureCode = x.UnitOfMeasureCode,
                });
            }
        });
        
        return responseList;
    }
}