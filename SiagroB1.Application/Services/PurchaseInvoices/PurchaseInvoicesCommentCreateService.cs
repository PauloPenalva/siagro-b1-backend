using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Inclui um comentário no documento de entrada. Sem guarda de status: comentário é anotação e vale
/// a qualquer tempo — inclusive em documento cancelado, onde registrar o motivo é justamente o uso
/// mais comum (ver <see cref="ContractCommentRules"/>).
/// </summary>
public class PurchaseInvoicesCommentCreateService(
    AppDbContext context,
    PurchaseInvoicesChangeLogService changeLog,
    ILogger<PurchaseInvoicesCommentCreateService> logger)
{
    public async Task<PurchaseInvoiceComment> ExecuteAsync(
        Guid purchaseInvoiceKey, string? commentText, string userName)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            if (!await context.PurchaseInvoices.AnyAsync(x => x.Key == purchaseInvoiceKey))
                throw new NotFoundException("Documento de entrada não encontrado.");

            var comment = new PurchaseInvoiceComment
            {
                PurchaseInvoiceKey = purchaseInvoiceKey,
                CommentedAt = DateTime.Now,
                CommentedBy = userName,
                CommentText = text,
            };

            await context.AddAsync(comment);

            changeLog.Register(
                purchaseInvoiceKey,
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
