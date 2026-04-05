using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.DocNumbers;

public class DocNumberGetInfoByTransactionCodeService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource
    )
{
    public async Task<ICollection<DocNumberInfoDto>> GetInfo(TransactionCode transactionCode)
    {
        return await db.Context.DocNumbers
                            .AsNoTracking()
                            .Where(o => o.TransactionCode == transactionCode )
                            .Select(o => new DocNumberInfoDto
                            {
                                Key = o.Key, 
                                Default = o.Default
                            })
                            .ToListAsync();
    }
}