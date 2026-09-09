using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Inclui um comentário na carga. Sem guarda de status: comentário é anotação e vale a qualquer
/// tempo — inclusive em carga cancelada, onde registrar o motivo é justamente o uso mais comum
/// (ver <see cref="ContractCommentRules"/>).
/// </summary>
public class ShipmentLoadsCommentCreateService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsCommentCreateService> logger)
{
    public async Task<ShipmentLoadComment> ExecuteAsync(
        Guid shipmentLoadKey, string? commentText, string userName)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            if (!await context.ShipmentLoads.AnyAsync(x => x.Key == shipmentLoadKey))
                throw new NotFoundException("Carga não encontrada.");

            var comment = new ShipmentLoadComment
            {
                ShipmentLoadKey = shipmentLoadKey,
                CommentedAt = DateTime.Now,
                CommentedBy = userName,
                CommentText = text,
            };

            await context.AddAsync(comment);

            changeLog.Register(
                shipmentLoadKey,
                ShipmentLoadChangeLogFields.Comment,
                null,
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
