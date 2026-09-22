using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

public class ContractDraftsGetService(AppDbContext context)
{
    public Task<List<ContractDraftDto>> ListByContractAsync(ContractDraftContractType contractType, Guid contractKey)
    {
        var query = contractType == ContractDraftContractType.Purchase
            ? context.ContractDrafts.Where(d => d.PurchaseContractKey == contractKey)
            : context.ContractDrafts.Where(d => d.SalesContractKey == contractKey);

        return query.AsNoTracking().OrderBy(d => d.Sequence).Select(d => new ContractDraftDto
        {
            Key = d.Key, ContractCode = d.ContractCode, Sequence = d.Sequence, TemplateKey = d.TemplateKey,
            TemplateName = d.Template!.Name, DraftType = d.DraftType, Description = d.Description, Status = d.Status,
            Provider = d.Provider, SentAt = d.SentAt, SignedAt = d.SignedAt, LastError = d.LastError,
            SignedAttachmentKey = d.SignedAttachmentKey, CreatedAt = d.CreatedAt, CreatedBy = d.CreatedBy,
            Signers = d.Signers.OrderBy(s => s.Side).ThenBy(s => s.Order).Select(s => new ContractDraftSignerDto
            {
                Key = s.Key, Side = s.Side, Name = s.Name, Email = s.Email, Role = s.Role, Order = s.Order,
                Status = s.Status, SignedAt = s.SignedAt, LastMessage = s.LastMessage,
            }).ToList(),
        }).ToListAsync();
    }

    public async Task<string> GetBodyAsync(Guid key) =>
        await context.ContractDrafts.AsNoTracking().Where(d => d.Key == key).Select(d => d.BodyHtml).FirstOrDefaultAsync()
        ?? throw new NotFoundException("Minuta não encontrada.");
}
