using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCopyService(
    AppDbContext db,
    PurchaseContractsCreateService createService,
    DocNumberSequenceService numberSequenceService)
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
        // A liberação de pagamento é do contrato original; a cópia recomeça Pendente. As
        // liberações por departamento (PaymentReleases) não entram no Include, logo não são copiadas.
        cloneContract.PaymentReleaseStatus = null;
        cloneContract.EarlyPaymentAuthorized = false;
        cloneContract.PaymentReleases.Clear();

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