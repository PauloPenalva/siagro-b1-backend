using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsReverseService(
    IUnitOfWork db,
    StorageAddressesGetBalanceService balanceService,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string username, TransactionCode transactionCode = TransactionCode.StorageTransaction)
    {
        var doc = await db.Context.StorageTransactions
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new NotFoundException(resource["EXCEPTION_00007"]);
        
        if (doc.TransactionOrigin == TransactionCode.StorageTransaction)
            transactionCode = TransactionCode.StorageTransaction;    
        
        if (doc.TransactionOrigin != transactionCode)
            throw new ApplicationException(resource["EXCEPTION_00008"] + doc.TransactionOrigin);
        
        if (doc.TransactionStatus == StorageTransactionsStatus.Invoiced)
            throw new ApplicationException(resource["EXCEPTION_00009"]);
        
        ValidateBalance(doc);

        try
        {
            doc.TransactionStatus = StorageTransactionsStatus.Pending;
            doc.CleaningDiscount = 0;
            doc.CleaningServicePrice = 0;
            doc.DryingDiscount = 0;
            doc.DryingServicePrice = 0;
            doc.OthersDicount = 0;
            doc.ShipmentPrice = 0;
            doc.ReceiptServicePrice = 0;
            doc.NetWeight = doc.GrossWeight;
            
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private void ValidateBalance(StorageTransaction doc)
    {
        if (string.IsNullOrEmpty(doc.StorageAddressCode))
            throw new ApplicationException(resource["EXCEPTION_000010"]);
        
        var balance = balanceService.GetBalance(doc.StorageAddressCode);
        if (balance < doc.NetWeight)
            throw new ApplicationException(resource["EXCEPTION_000011"]);
    }
}