using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Altera o texto de um comentário do contrato de compra. A data/hora e o autor são REESCRITOS: a
/// linha passa a mostrar a última alteração, e a versão anterior sobrevive no log.
/// </summary>
public class PurchaseContractsCommentUpdateService(
    AppDbContext context,
    PurchaseContractsChangeLogService changeLog,
    ILogger<PurchaseContractsCommentUpdateService> logger)
{
    public async Task<PurchaseContractComment> ExecuteAsync(
        Guid commentKey, string? commentText, string userName, bool isAdmin)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            var comment = await context.PurchaseContractsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comment not found");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            var previousText = comment.CommentText;

            comment.CommentText = text;
            comment.CommentedAt = DateTime.Now;
            comment.CommentedBy = userName;

            if (comment.PurchaseContractKey.HasValue)
                changeLog.Register(
                    comment.PurchaseContractKey.Value,
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
