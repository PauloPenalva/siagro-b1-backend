using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Inclui um comentário no contrato de venda. Sem guarda de status: comentário é anotação e vale a
/// qualquer tempo (ver <see cref="ContractCommentRules"/>).
/// </summary>
public class SalesContractsCommentCreateService(
    AppDbContext context,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsCommentCreateService> logger)
{
    public async Task<SalesContractComment> ExecuteAsync(
        Guid salesContractKey, string? commentText, string userName)
    {
        try
        {
            var text = ContractCommentRules.NormalizeText(commentText);

            if (!await context.SalesContracts.AnyAsync(x => x.Key == salesContractKey))
                throw new NotFoundException("Sales contract not found");

            var comment = new SalesContractComment
            {
                SalesContractKey = salesContractKey,
                CommentedAt = DateTime.Now,
                CommentedBy = userName,
                CommentText = text,
            };

            await context.AddAsync(comment);

            changeLog.Register(
                salesContractKey,
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
