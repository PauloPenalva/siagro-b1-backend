using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.DocTypes;

public class DocTypesService(IUnitOfWork db)
{
    public async Task<DocType> GetDocType(TransactionCode transactionCode)
    {
        return await db.Context.DocTypes
            .AsNoTracking()
            .Where(x => x.TransactionCode == transactionCode)
            .FirstOrDefaultAsync() ??
                throw new NotFoundException("DocType not found.");
    }
    
    
    public async Task<int> GetNextNumber(string docTypeCode, TransactionCode transactionCode)
    {
        return await db.Context.DocTypes
            .AsNoTracking()
            .Where(x => x.Code == docTypeCode && x.TransactionCode == transactionCode)
            .Select(x => x.NextNumber)
            .FirstOrDefaultAsync();
    }
    
    public async Task<string> GetSerie(string docTypeCode, TransactionCode transactionCode)
    {
        return await db.Context.DocTypes
            .AsNoTracking()
            .Where(x => x.Code == docTypeCode && x.TransactionCode == transactionCode)
            .Select(x => x.Serie)
            .FirstOrDefaultAsync() ?? string.Empty;
    }
    
    
    public async Task UpdateLastNumber(string docTypeCode, int currenctNumber, TransactionCode transactionCode)
    {
        if (docTypeCode == null)
        {
            throw new ApplicationException("DocType Code not informed.");
        }

        var docType = await db.Context.DocTypes
                          .Where(x => x.Code == docTypeCode && x.TransactionCode == transactionCode)
                          .FirstOrDefaultAsync() ??
                      throw new NotFoundException("DocType not found.");
        
        docType.LastNumber = currenctNumber;
        docType.NextNumber = ++currenctNumber;
        
        await db.SaveChangesAsync();
    }
}