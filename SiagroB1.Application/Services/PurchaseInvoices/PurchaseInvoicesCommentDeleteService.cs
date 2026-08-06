using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Exclui um comentário do documento de entrada. O texto excluído fica registrado no log — é o que
/// permite reconstituir o que foi apagado.
/// </summary>
public class PurchaseInvoicesCommentDeleteService(
    AppDbContext context,
    PurchaseInvoicesChangeLogService changeLog,
    ILogger<PurchaseInvoicesCommentDeleteService> logger)
{
    public async Task ExecuteAsync(Guid commentKey, string userName, bool isAdmin)
    {
        try
        {
            var comment = await context.PurchaseInvoicesComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comentário não encontrado.");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            context.PurchaseInvoicesComments.Remove(comment);

            if (comment.PurchaseInvoiceKey.HasValue)
                changeLog.Register(
                    comment.PurchaseInvoiceKey.Value,
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
