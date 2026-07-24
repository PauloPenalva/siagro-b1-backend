using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsAttachmentsCreateService(
    IUnitOfWork db,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsAttachmentsCreateService> logger)
{
    public async Task SaveAsync(Guid contractKey, SalesContractAttachment attachment, string userName)
    {
        try
        {
            var contract = await db.Context.SalesContracts
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new ApplicationException($"Contrato de venda não encontrado.");

            SalesContractsPostApprovalGuard.EnsureEditable(contract);

            contract.AddAttachment(attachment);

            changeLog.Register(
                contractKey, ContractChangeLogFields.Attachment,
                null, attachment.FileName, userName);

            await db.Context.SaveChangesAsync();
        }
        catch (DefaultException)
        {
            // Recusa de regra (status do contrato): precisa chegar ao controller como
            // DefaultException para virar 400, não ser reembrulhada em 500.
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e.Message, e);
            throw new ApplicationException(e.Message);
        }
    }
}