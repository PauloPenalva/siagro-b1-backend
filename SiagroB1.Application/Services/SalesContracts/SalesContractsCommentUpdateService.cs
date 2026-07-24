using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Altera o texto de um comentário do contrato de venda. A data/hora e o autor são REESCRITOS: a
/// linha passa a mostrar a última alteração, e a versão anterior sobrevive no log.
/// </summary>
public class SalesContractsCommentUpdateService(
    AppDbContext context,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsCommentUpdateService> logger)
{
    public async Task<SalesContractComment> ExecuteAsync(
        Guid commentKey, string? commentText, string userName, bool isAdmin)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            var comment = await context.SalesContractsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comment not found");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            var previousText = comment.CommentText;

            comment.CommentText = text;
            comment.CommentedAt = DateTime.Now;
            comment.CommentedBy = userName;

            if (comment.SalesContractKey.HasValue)
                changeLog.Register(
                    comment.SalesContractKey.Value,
                    ContractChangeLogFields.Comment,
                    previousText,
                    text,
                    userName);

            await context.SaveChangesAsync();

            return comment;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
