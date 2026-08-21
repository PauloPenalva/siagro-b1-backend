using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsAttachmentsCreateService(
    IUnitOfWork db,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsAttachmentsCreateService> logger)
{
    /// <summary>
    /// Anexo é documentação do contrato, não movimento: aceito em QUALQUER status, inclusive
    /// encerrado e cancelado. Por isso não passa pelo <see cref="SalesContractsPostApprovalGuard"/>,
    /// que segue valendo para os locais de entrega.
    /// </summary>
    public async Task SaveAsync(Guid contractKey, SalesContractAttachment attachment, string userName)
    {
        try
        {
            var contract = await db.Context.SalesContracts
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new ApplicationException($"Contrato de venda não encontrado.");

            contract.AddAttachment(attachment);

            changeLog.Register(
                contractKey, ContractChangeLogFields.Attachment,
                null, attachment.FileName, userName);

            await db.Context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message, e);
            throw new ApplicationException(e.Message);
        }
    }
}