using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Exclui um comentário do contrato de venda. O texto excluído fica registrado no log — é o que
/// permite reconstituir o que foi apagado.
/// </summary>
public class SalesContractsCommentDeleteService(
    AppDbContext context,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsCommentDeleteService> logger)
{
    public async Task ExecuteAsync(Guid commentKey, string userName, bool isAdmin)
    {
        try
        {
            var comment = await context.SalesContractsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comment not found");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            context.SalesContractsComments.Remove(comment);

            if (comment.SalesContractKey.HasValue)
                changeLog.Register(
                    comment.SalesContractKey.Value,
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
