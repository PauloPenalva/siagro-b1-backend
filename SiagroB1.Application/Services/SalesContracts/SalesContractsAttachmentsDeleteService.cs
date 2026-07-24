using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsAttachmentsDeleteService(
    IUnitOfWork db,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsAttachmentsDeleteService> logger)
{
    public async Task Delete(Guid key, string userName)
    {
        var attachment = await db.Context.SalesContractAttachments
                             .Include(x => x.SalesContract)
                             .FirstOrDefaultAsync(x => x.Key == key) ??
                         throw new NotFoundException($"Anexo não encontrado.");

        if (attachment.SalesContract is not null)
        {
            SalesContractsPostApprovalGuard.EnsureEditable(attachment.SalesContract);
        }

        try
        {
            db.Context.SalesContractAttachments.Remove(attachment);

            if (attachment.SalesContractKey is { } contractKey)
            {
                changeLog.Register(
                    contractKey, ContractChangeLogFields.Attachment,
                    attachment.FileName, null, userName);
            }

            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}