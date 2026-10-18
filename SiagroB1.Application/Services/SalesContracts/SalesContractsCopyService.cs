using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsCopyService(
    AppDbContext db,
    SalesContractsCreateService createService,
    DocNumberSequenceService numberSequenceService)
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
        // Número manual do original não se repete na cópia: a cópia nasce com número automático.
        cloneContract.Code = null;
        // A série do original só é herdada se ainda for automática e ativa; senão vale a padrão.
        cloneContract.DocNumberKey = await numberSequenceService.ResolveInheritedKeyAsync(cloneContract.DocNumberKey);
        cloneContract.CreationDate = DateTime.Now.Date;
        cloneContract.Status = ContractStatus.Draft;
        cloneContract.CreationDate = DateTime.Now.Date;
        cloneContract.ApprovalComments = string.Empty;

        await createService.ExecuteAsync(cloneContract, userName);
    }
}