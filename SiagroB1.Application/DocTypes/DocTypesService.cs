using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.DocTypes;

public class DocTypesService(AppDbContext context)
{
    public async Task<int> GetNextNumber(string docTypeCode, TransactionCode transactionCode)
    {
        return await context.DocTypes
            .AsNoTracking()
            .Where(x => x.Code == docTypeCode && x.TransactionCode == transactionCode)
            .Select(x => x.NextNumber)
            .FirstOrDefaultAsync();
    }
    
    
    public async Task UpdateLastNumber(string docTypeCode, int currenctNumber, TransactionCode transactionCode)
    {
        if (docTypeCode == null)
        {
            throw new ApplicationException("DocType Code not informed.");
        }

        var docType = await context.DocTypes
                          .Where(x => x.Code == docTypeCode && x.TransactionCode == transactionCode)
                          .FirstOrDefaultAsync() ??
                      throw new NotFoundException("DocType not found.");
        
        docType.LastNumber = currenctNumber;
        docType.NextNumber = ++currenctNumber;
        
        await context.SaveChangesAsync();
    }
}