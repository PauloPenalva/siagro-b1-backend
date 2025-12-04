using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCopyService(AppDbContext db, PurchaseContractsCreateService createService)
{
    public async Task ExecuteAsync(Guid key, string userName) 
    {
        var originalContract = await db.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.PriceFixations)
            .Include(x => x.Taxes)
            .Include(x => x.QualityParameters)
            .Include(x => x.Brokers)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                               throw new NotFoundException("Purchase contract not found.");

        
        var cloneContract = JsonSerializer.Deserialize<PurchaseContract>(
            JsonSerializer.Serialize(originalContract, 
                new JsonSerializerOptions 
                { 
                    ReferenceHandler = ReferenceHandler.IgnoreCycles 
                })) ??  throw new ApplicationException("Error on copying purchase contract.");

        cloneContract.Key = null;
        cloneContract.Status = ContractStatus.Draft;
        cloneContract.CreationDate = DateTime.Now.Date;

        cloneContract.PriceFixations.Clear();
        
        foreach (var tax in cloneContract.Taxes)
        {
            tax.Key = null;
            tax.PurchaseContract = cloneContract;
        }
        
        foreach (var broker in cloneContract.Brokers)
        {
            broker.Key = null;
            broker.PurchaseContract = cloneContract;
        } 
        
        foreach (var parameter in cloneContract.QualityParameters)
        {
            parameter.Key = null;
            parameter.PurchaseContract = cloneContract;
        }

        await createService.ExecuteAsync(cloneContract, userName, true);
    }
}