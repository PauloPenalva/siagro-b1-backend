using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui um comentário da carga. O texto excluído fica registrado no log — é o que permite
/// reconstituir o que foi apagado.
/// </summary>
public class ShipmentLoadsCommentDeleteService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsCommentDeleteService> logger)
{
    public async Task ExecuteAsync(Guid commentKey, string userName, bool isAdmin)
    {
        try
        {
            var comment = await context.ShipmentLoadsComments.FindAsync(commentKey)
                ?? throw new NotFoundException("Comentário não encontrado.");

            ContractCommentRules.EnsureCanModify(comment.CommentedBy, userName, isAdmin);

            context.ShipmentLoadsComments.Remove(comment);

            if (comment.ShipmentLoadKey.HasValue)
                changeLog.Register(
                    comment.ShipmentLoadKey.Value,
                    ShipmentLoadChangeLogFields.Comment,
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
