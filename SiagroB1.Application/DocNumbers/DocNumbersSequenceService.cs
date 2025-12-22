using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.DocNumbers;

public class DocNumbersSequenceService(IUnitOfWork db)
{
    public async Task<IEnumerable<DocNumber>> GetDocNumbersSeries(TransactionCode transactionCode)
    {
        return await db.Context.DocNumbers
            .AsNoTracking()
            .Where(x => x.TransactionCode == transactionCode && x.Inactive == false)
            .ToListAsync();
    }
    
    public async Task<DocNumber> GetDocNumber(Guid key)
    {
        return await db.Context.DocNumbers
            .FirstOrDefaultAsync(x => x.Key == key) ??
                throw new NotFoundException("DocType not found.");
    }
    
    public async Task UpdateLastNumber(Guid key, int currenctNumber)
    {
        var docType = await db.Context.DocNumbers
                          .FirstOrDefaultAsync(x => x.Key == key) ??
                      throw new NotFoundException("DocType not found.");
        
        docType.LastNumber = currenctNumber;
        docType.NextNumber = ++currenctNumber;
        
        await db.SaveChangesAsync();
    }

    public static string FormatNumber(int currentNumber, int size, string prefix = "", string suffix = "")
    {
        var formatedNumber = currentNumber
            .ToString()
            .PadLeft(size, '0');

        return $"{prefix}{formatedNumber}{suffix}";

    }
}