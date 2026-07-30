using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsGetService(IUnitOfWork unitOfWork, ILogger<PurchaseContractsGetService> logger)
{
    public async Task<PurchaseContract?> GetByIdAsync(Guid key)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", key);
            return await unitOfWork.Context.PurchaseContracts
                .Include(p => p.PriceFixations)
                .Include(p => p.QualityParameters)
                    .ThenInclude(qa => qa.QualityAttrib)
                .Include(p => p.Taxes)
                    .ThenInclude(t => t.Tax)
                .Include(x => x.ShipmentReleases)
                .Include(x => x.HarvestSeason)
                .Include(x => x.LogisticRegion)
                .Include(x => x.DocNumber)
                .Include(x => x.Brokers)
                .Include(x => x.Allocations)
                    .ThenInclude(x => x.StorageTransaction)
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
        return unitOfWork.Context.PurchaseContracts
            .AsNoTracking()
            .Include(p => p.Allocations)
                .ThenInclude(a => a.StorageTransaction);
    }

    public List<PurchaseContractDto> GetAvaiablesPurchaseContracts(string cardCode, string itemCode)
    {
        var responseList = new List<PurchaseContractDto>();
        
        var contractsList = unitOfWork.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.Allocations)
                .ThenInclude(a => a.StorageTransaction)
            .Where(p => p.CardCode == cardCode && p.ItemCode == itemCode && (p.Status == ContractStatus.Approved || p.Status == ContractStatus.Finished )) 
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
                    AgentCode = x.AgentCode,
                    AgentName = x.AgentName,
                });
            }
        });
        
        return responseList;
    }
}