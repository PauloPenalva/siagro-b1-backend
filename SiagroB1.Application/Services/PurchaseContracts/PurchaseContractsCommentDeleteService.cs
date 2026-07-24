using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Exclui um comentário do contrato de compra. O texto excluído fica registrado no log — é o que
/// permite reconstituir o que foi apagado.
/// </summary>
public class PurchaseContractsCommentDeleteService(
    AppDbContext context,
    PurchaseContractsChangeLogService changeLog,
    ILogger<PurchaseContractsCommentDeleteService> logger)
{
    public async Task ExecuteAsync(Guid commentKey, string userName, bool isAdmin)
    {
        try
        {
            var comment = await context.PurchaseContractsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comment not found");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            context.PurchaseContractsComments.Remove(comment);

            if (comment.PurchaseContractKey.HasValue)
                changeLog.Register(
                    comment.PurchaseContractKey.Value,
                    ContractChangeLogFields.Comment,
                    comment.CommentText,
                    null,
                    userName);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
