using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Altera o texto de um comentário da carga. A data/hora e o autor são REESCRITOS: a linha passa
/// a mostrar a última alteração, e a versão anterior sobrevive no log.
/// </summary>
public class ShipmentLoadsCommentUpdateService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsCommentUpdateService> logger)
{
    public async Task<ShipmentLoadComment> ExecuteAsync(
        Guid commentKey, string? commentText, string userName, bool isAdmin)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            var comment = await context.ShipmentLoadsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comentário não encontrado.");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            var previousText = comment.CommentText;

            comment.CommentText = text;
            comment.CommentedAt = DateTime.Now;
            comment.CommentedBy = userName;

            if (comment.ShipmentLoadKey.HasValue)
                changeLog.Register(
                    comment.ShipmentLoadKey.Value,
                    ShipmentLoadChangeLogFields.Comment,
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
