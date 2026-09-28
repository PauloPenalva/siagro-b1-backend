using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Envia a minuta para assinatura. Síncrono de propósito: o usuário aperta o botão e espera a
/// resposta do provedor, como na Tagui.
///
/// A chamada HTTP acontece ANTES do SaveChanges e o resultado decide o que se grava — não existe
/// estado intermediário "enviando" que um processo morto deixaria para trás.
/// </summary>
public class ContractDraftsSendToSignatureService(
    AppDbContext context,
    ContractDraftsLoader loader,
    ContractDraftsGetPdfService pdfService,
    IESignatureProvider provider,
    IConfiguration configuration,
    PurchaseContractsChangeLogService purchaseLog,
    SalesContractsChangeLogService salesLog,
    PurchaseContractsSetSignatureStatusService purchaseSignatureStatus,
    SalesContractsSetSignatureStatusService salesSignatureStatus,
    ILogger<ContractDraftsSendToSignatureService> logger)
{
    public const string DisabledMessage = "Assinatura eletrônica não está habilitada neste ambiente.";
    public const string NoCompanySignatoryMessage = "Nenhum signatário ativo da empresa para esta filial.";
    public const string NoPartnerSignatoryMessage = "Nenhum signatário ativo cadastrado para o parceiro.";

    public async Task ExecuteAsync(Guid key, string userName, CancellationToken ct = default)
    {
        if (!configuration.GetValue("Signature:Enabled", false))
            throw new BusinessException(DisabledMessage);

        var draft = await loader.RequireDraftAsync(key, ct);
        ContractDraftsLoader.RequireEditable(draft);

        var cardCode = await ResolveCardCodeAsync(draft, ct);

        var company = await context.CompanySignatories.AsNoTracking()
            .Where(s => s.Active && (s.BranchCode == draft.BranchCode || s.BranchCode == null))
            .OrderBy(s => s.Order).ToListAsync(ct);
        if (company.Count == 0) throw new BusinessException(NoCompanySignatoryMessage);

        var partner = await context.BusinessPartnerSignatories.AsNoTracking()
            .Where(s => s.Active && s.CardCode == cardCode)
            .OrderBy(s => s.Order).ToListAsync(ct);
        if (partner.Count == 0) throw new BusinessException(NoPartnerSignatoryMessage);

        var (pdf, fileName) = await pdfService.ExecuteAsync(draft.Key, ct);

        // Empresa primeiro, parceiro depois; Order contínuo, que é o que vai ao provedor.
        var order = 0;
        var signers = new List<ContractDraftSigner>();
        foreach (var s in company)
            signers.Add(new ContractDraftSigner { DraftKey = draft.Key, Side = SignerSide.Company, Name = s.Name, TaxId = s.TaxId, Email = s.Email, Role = s.Role, Order = ++order });
        foreach (var s in partner)
            signers.Add(new ContractDraftSigner { DraftKey = draft.Key, Side = SignerSide.Partner, Name = s.Name, TaxId = s.TaxId, Email = s.Email, Role = s.Role, Order = ++order });

        var request = new ESignatureSendRequest(
            Title: draft.Description,
            FileName: fileName,
            PdfBytes: pdf,
            Signers: signers.Select(s => new ESignatureSigner(s.Name, s.Email, s.Role, s.Order)).ToList(),
            WebhookUrl: WebhookUrl(),
            Message: $"Contrato {draft.ContractCode} — minuta {draft.Sequence}. Por favor, assine.");

        var result = await provider.SendAsync(request, ct);

        if (!result.Succeeded)
        {
            // Só o erro é gravado: o snapshot de signers nunca chega ao contexto.
            draft.LastError = result.ErrorMessage;
            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;
            await context.SaveChangesAsync(ct);

            logger.LogWarning("Minuta {Key} não foi enviada ao provedor. Transitório: {Transient}", key, result.Transient);
            throw new BusinessException(result.ErrorMessage ?? "Não foi possível enviar a minuta para assinatura.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            context.ContractDraftSigners.AddRange(signers);

            draft.Provider = provider.Name;
            draft.ExternalDocumentId = result.ExternalDocumentId;
            draft.SentAt = DateTime.Now;
            draft.Status = ContractDraftStatus.AwaitingSignature;
            draft.LastError = null;
            draft.UpdatedAt = DateTime.Now;
            draft.UpdatedBy = userName;

            var what = ContractChangeLogFields.DescribeDraft(draft.Sequence, "enviada para assinatura");
            if (draft.PurchaseContractKey is { } pk)
            {
                purchaseLog.Register(pk, ContractChangeLogFields.Draft, null, what, userName);
                await purchaseSignatureStatus.ExecuteAsync(pk, SignatureStatus.AwaitingSignature, userName);
            }
            if (draft.SalesContractKey is { } sk)
            {
                salesLog.Register(sk, ContractChangeLogFields.Draft, null, what, userName);
                await salesSignatureStatus.ExecuteAsync(sk, SignatureStatus.AwaitingSignature, userName);
            }

            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync(ct);
            logger.LogError(e, "Minuta {Key} foi aceita pelo provedor mas não pôde ser gravada. Documento: {Uuid}",
                key, result.ExternalDocumentId);

            // O rollback devolve a minuta a Draft SEM ExternalDocumentId, mas o D4Sign já tem o
            // documento e JÁ mandou e-mail aos signatários: a reconciliação não o acha, o webhook
            // cai no ramo de uuid desconhecido, e a minuta volta a ser enviável — gerando um
            // segundo documento e uma segunda rodada de e-mails às mesmas contrapartes. Cancelar
            // é best-effort e nunca pode mascarar o erro original; é o mesmo que o provider já faz
            // no AbortAsync dele quando um passo do envio falha depois do upload.
            if (!string.IsNullOrWhiteSpace(result.ExternalDocumentId))
            {
                try
                {
                    await provider.CancelAsync(result.ExternalDocumentId, ct);
                }
                catch (Exception cancelError)
                {
                    logger.LogWarning(cancelError,
                        "Documento {Uuid} ficou órfão no provedor: o cancelamento depois da falha de gravação não funcionou",
                        result.ExternalDocumentId);
                }
            }

            throw;
        }
    }

    private async Task<string?> ResolveCardCodeAsync(ContractDraft draft, CancellationToken ct) =>
        draft.PurchaseContractKey is { } pk
            ? await context.PurchaseContracts.Where(c => c.Key == pk).Select(c => c.CardCode).FirstOrDefaultAsync(ct)
            : await context.SalesContracts.Where(c => c.Key == draft.SalesContractKey).Select(c => c.CardCode).FirstOrDefaultAsync(ct);

    /// <summary>Vazia quando não há URL pública configurada: aí só a reconciliação atualiza o estado.</summary>
    private string WebhookUrl()
    {
        var baseUrl = configuration["Signature:D4Sign:WebhookBaseUrl"];
        var secret = configuration["Signature:D4Sign:WebhookSecret"];
        return string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(secret)
            ? ""
            : $"{baseUrl.TrimEnd('/')}/hooks/d4sign/{secret}";
    }
}
