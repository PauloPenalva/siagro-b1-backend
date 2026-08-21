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
    /// <summary>
    /// Espelha o create: anexo pode ser removido em QUALQUER status do contrato, inclusive
    /// encerrado e cancelado — nada de <see cref="SalesContractsPostApprovalGuard"/> aqui.
    /// </summary>
    public async Task Delete(Guid key, string userName)
    {
        var attachment = await db.Context.SalesContractAttachments
                             .FirstOrDefaultAsync(x => x.Key == key) ??
                         throw new NotFoundException($"Anexo não encontrado.");

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