using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsSendToSignatureServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    private async Task<ContractDraft> DraftAsync()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        return await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
    }

    [Fact]
    public async Task Sends_stores_the_snapshot_and_moves_the_contract_signature_status()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        _ctx.Signature.SucceedsWith("UUID-9");

        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        var saved = await _ctx.Db.Context.ContractDrafts.Include(d => d.Signers).SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.AwaitingSignature, saved.Status);
        Assert.Equal("UUID-9", saved.ExternalDocumentId);
        Assert.Equal("D4SignFake", saved.Provider);
        Assert.NotNull(saved.SentAt);
        Assert.Null(saved.LastError);
        Assert.Equal(2, saved.Signers.Count);
        Assert.Equal(SignerSide.Company, saved.Signers.OrderBy(s => s.Side).First().Side);
        Assert.All(saved.Signers, s => Assert.Equal(SignerStatus.Pending, s.Status));

        var contract = await _ctx.Db.Context.PurchaseContracts.SingleAsync(c => c.Key == saved.PurchaseContractKey);
        Assert.Equal(SignatureStatus.AwaitingSignature, contract.SignatureStatus);

        var log = await _ctx.Db.Context.PurchaseContractsChangeLogs
            .Where(l => l.Field == ContractChangeLogFields.Draft).ToListAsync();
        Assert.Contains(log, l => l.NewValue == "Minuta 1 enviada para assinatura");
    }

    [Fact]
    public async Task The_pdf_and_the_webhook_url_reach_the_provider()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();

        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        var request = _ctx.Signature.LastSendRequest!;
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(request.PdfBytes));
        Assert.Equal("PC-000123-minuta-1.pdf", request.FileName);
        Assert.Equal("https://gw.example.com/hooks/d4sign/SEGREDO", request.WebhookUrl);
        Assert.Equal(2, request.Signers.Count);
    }

    [Fact]
    public async Task Provider_failure_keeps_the_draft_records_the_error_and_discards_the_signers()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        _ctx.Signature.FailsTransiently("D4Sign respondeu 503");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Contains("503", ex.Message);
        var saved = await _ctx.Db.Context.ContractDrafts.Include(d => d.Signers).SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Draft, saved.Status);
        Assert.Contains("503", saved.LastError);
        Assert.Empty(saved.Signers);
        Assert.Empty(_ctx.Db.Context.ContractDraftSigners);
    }

    [Fact]
    public async Task Refuses_to_send_twice()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Minuta já enviada para assinatura não pode ser alterada.", ex.Message);
        Assert.Equal(1, _ctx.Signature.SendCalls);
    }

    [Fact]
    public async Task Refuses_without_a_company_signatory()
    {
        var draft = await DraftAsync();
        _ctx.Db.Context.BusinessPartnerSignatories.Add(new BusinessPartnerSignatory
        {
            CardCode = "F0001", Name = "Produtor", TaxId = "55566677788", Email = "produtor@x.com",
            Role = SignatoryRole.SignAsParty, Order = 1, Active = true,
        });
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Nenhum signatário ativo da empresa para esta filial.", ex.Message);
        Assert.Equal(0, _ctx.Signature.SendCalls);
    }

    [Fact]
    public async Task Refuses_without_a_partner_signatory()
    {
        var draft = await DraftAsync();
        _ctx.Db.Context.CompanySignatories.Add(new CompanySignatory
        {
            Name = "Diretor", TaxId = "11122233344", Email = "diretor@tagui.com",
            Role = SignatoryRole.SignAsParty, Order = 1, BranchCode = "01", Active = true,
        });
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Nenhum signatário ativo cadastrado para o parceiro.", ex.Message);
    }

    [Fact]
    public async Task Refuses_when_signature_is_disabled_in_this_environment()
    {
        var draft = await DraftAsync();
        await _ctx.SeedSignatoriesAsync();
        _ctx.Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Signature:Enabled"] = "false" }).Build();

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default));

        Assert.Equal("Assinatura eletrônica não está habilitada neste ambiente.", ex.Message);
        Assert.Equal(0, _ctx.Signature.SendCalls);
    }
}
