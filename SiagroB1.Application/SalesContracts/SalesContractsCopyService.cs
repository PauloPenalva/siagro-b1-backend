using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsCopyService(AppDbContext db, SalesContractsCreateService createService)
{
    public async Task ExecuteAsync(Guid key, string userName) 
    {
        var originalContract = await db.SalesContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                               throw new NotFoundException("Sales contract not found.");

        
        var cloneContract = JsonSerializer.Deserialize<SalesContract>(
            JsonSerializer.Serialize(originalContract, 
                new JsonSerializerOptions 
                { 
                    ReferenceHandler = ReferenceHandler.IgnoreCycles 
                })) ??  throw new ApplicationException("Error on copying sales contract.");

        cloneContract.Key = Guid.Empty;
        cloneContract.RowId = 0;
        cloneContract.CreationDate = DateTime.Now.Date;
        cloneContract.Status = ContractStatus.Draft;
        cloneContract.CreationDate = DateTime.Now.Date;
        cloneContract.ApprovalComments = string.Empty;

        await createService.ExecuteAsync(cloneContract, userName);
    }
}