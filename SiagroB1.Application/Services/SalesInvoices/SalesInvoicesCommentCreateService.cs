using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Inclui um comentário no documento de saída. Sem guarda de status: comentário é anotação e vale a
/// qualquer tempo (ver <see cref="ContractCommentRules"/>).
/// </summary>
public class SalesInvoicesCommentCreateService(
    AppDbContext context,
    SalesInvoicesChangeLogService changeLog,
    ILogger<SalesInvoicesCommentCreateService> logger)
{
    public async Task<SalesInvoiceComment> ExecuteAsync(
        Guid salesInvoiceKey, string? commentText, string userName)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            if (!await context.SalesInvoices.AnyAsync(x => x.Key == salesInvoiceKey))
                throw new NotFoundException("Sales invoice not found");

            var comment = new SalesInvoiceComment
            {
                SalesInvoiceKey = salesInvoiceKey,
                CommentedAt = DateTime.Now,
                CommentedBy = userName,
                CommentText = text,
            };

            await context.AddAsync(comment);

            changeLog.Register(
                salesInvoiceKey,
                ContractChangeLogFields.Comment,
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
