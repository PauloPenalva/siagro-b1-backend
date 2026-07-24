using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Inclui um comentário no contrato de compra. Sem guarda de status: comentário é anotação e vale a
/// qualquer tempo, inclusive em contrato encerrado — por isso a entidade também está fora do
/// FinishedContractMutationGuardInterceptor (ver <see cref="ContractCommentRules"/>).
/// </summary>
public class PurchaseContractsCommentCreateService(
    AppDbContext context,
    PurchaseContractsChangeLogService changeLog,
    ILogger<PurchaseContractsCommentCreateService> logger)
{
    public async Task<PurchaseContractComment> ExecuteAsync(
        Guid purchaseContractKey, string? commentText, string userName)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            if (!await context.PurchaseContracts.AnyAsync(x => x.Key == purchaseContractKey))
                throw new NotFoundException("Purchase contract not found");

            var comment = new PurchaseContractComment
            {
                PurchaseContractKey = purchaseContractKey,
                CommentedAt = DateTime.Now,
                CommentedBy = userName,
                CommentText = text,
            };

            await context.AddAsync(comment);

            changeLog.Register(
                purchaseContractKey,
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
