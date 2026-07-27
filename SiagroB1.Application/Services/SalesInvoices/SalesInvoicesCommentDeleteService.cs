using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Exclui um comentário do documento de saída. O texto excluído fica registrado no log — é o que
/// permite reconstituir o que foi apagado.
/// </summary>
public class SalesInvoicesCommentDeleteService(
    AppDbContext context,
    SalesInvoicesChangeLogService changeLog,
    ILogger<SalesInvoicesCommentDeleteService> logger)
{
    public async Task ExecuteAsync(Guid commentKey, string userName, bool isAdmin)
    {
        try
        {
            var comment = await context.SalesInvoicesComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comment not found");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            context.SalesInvoicesComments.Remove(comment);

            if (comment.SalesInvoiceKey.HasValue)
                changeLog.Register(
                    comment.SalesInvoiceKey.Value,
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
