using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Cria a minuta: resolve os placeholders, renderiza e grava HTML + JSON como snapshot. Aceita
/// qualquer status de contrato exceto cancelado — aditivo e distrato existem justamente para
/// contrato aprovado ou encerrado. Log do contrato na mesma transação.
/// </summary>
public class ContractDraftsCreateService(
    AppDbContext context,
    ContractDraftPlaceholderResolver resolver,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    ILogger<ContractDraftsCreateService> logger)
{
    public async Task<ContractDraft> ExecuteAsync(
        ContractDraftContractType contractType, Guid contractKey, Guid templateKey,
        ContractDraftType draftType, string description, string userName, CancellationToken ct)
    {
        var template = await context.ContractTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == templateKey, ct)
                       ?? throw new NotFoundException("Modelo de contrato não encontrado.");
        if (!template.Active)
            throw new BusinessException("Modelo inativo.");

        var draft = new ContractDraft
        {
            TemplateKey = template.Key,
            DraftType = draftType,
            Description = string.IsNullOrWhiteSpace(description) ? template.Title : description.Trim(),
            ContractCode = "",
            BodyHtml = "",
            PlaceholdersJson = "",
            Status = ContractDraftStatus.Draft,
            CreatedAt = DateTime.Now, CreatedBy = userName, UpdatedAt = DateTime.Now, UpdatedBy = userName,
        };

        Dictionary<string, string> values;

        if (contractType == ContractDraftContractType.Purchase)
        {
            var contract = await context.PurchaseContracts
                .Include(c => c.Brokers).FirstOrDefaultAsync(c => c.Key == contractKey, ct)
                ?? throw new NotFoundException("Contrato de compra não encontrado.");
            RequireNotCanceled(contract.Status);
            if (template.ContractType == ContractTemplateScope.Sales)
                throw new BusinessException("Modelo não se aplica a contrato de compra.");

            values = await resolver.ResolveAsync(contract, ct);
            draft.PurchaseContractKey = contract.Key;
            draft.ContractCode = contract.Code ?? "";
            draft.BranchCode = contract.BranchCode;
            draft.Sequence = 1 + (await context.ContractDrafts
                .Where(d => d.PurchaseContractKey == contract.Key).MaxAsync(d => (int?)d.Sequence, ct) ?? 0);
        }
        else
        {
            var contract = await context.SalesContracts
                .Include(c => c.DeliveryLocations).FirstOrDefaultAsync(c => c.Key == contractKey, ct)
                ?? throw new NotFoundException("Contrato de venda não encontrado.");
            RequireNotCanceled(contract.Status);
            if (template.ContractType == ContractTemplateScope.Purchase)
                throw new BusinessException("Modelo não se aplica a contrato de venda.");

            values = await resolver.ResolveAsync(contract, ct);
            draft.SalesContractKey = contract.Key;
            draft.ContractCode = contract.Code ?? "";
            draft.BranchCode = contract.BranchCode;
            draft.Sequence = 1 + (await context.ContractDrafts
                .Where(d => d.SalesContractKey == contract.Key).MaxAsync(d => (int?)d.Sequence, ct) ?? 0);
        }

        draft.BodyHtml = ContractDraftTemplateRenderer.Render(template.BodyHtml, values);
        draft.PlaceholdersJson = JsonSerializer.Serialize(values);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            context.ContractDrafts.Add(draft);

            var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "criada");
            if (draft.PurchaseContractKey is { } pk) purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
            if (draft.SalesContractKey is { } sk) salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return draft;
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Falha ao criar minuta do contrato {ContractKey}", contractKey);
            throw;
        }
    }

    private static void RequireNotCanceled(ContractStatus? status)
    {
        if (status == ContractStatus.Canceled)
            throw new BusinessException("Contrato cancelado não recebe minuta.");
    }
}
