using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Adiantamento SEM contrato (spec 2026-10-19, parte 3 e decisões 1–2): abate qualquer título não cancelado do mesmo
/// parceiro, direção e moeda que não seja adiantamento; o adiantamento COM contrato segue só o seu contrato.
/// </summary>
public class FinancialAdvancesWithoutContractTests
{
    private static async Task<FinancialDocument> AddFreeAdvanceAsync(
        AdvanceApplicationSeed seed, decimal amount = 5_000m, string code = "FN-ADT-LIVRE",
        FinancialDirection direction = FinancialDirection.Payable, CurrencyType currency = CurrencyType.Brl,
        string cardCode = FiscalReconciliationSeed.Supplier)
    {
        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = "PRODUTOR TESTE",
            Direction = direction, Nature = FinancialDocumentNature.Advance, Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 12, 31), NetAmount = amount, OriginType = FinancialDocumentOrigin.Manual,
            BranchCode = seed.Contract.BranchCode, Currency = currency, PaymentTermsText = "",
        };
        seed.Context.FinancialDocuments.Add(advance);
        await seed.Context.SaveChangesAsync();
        await seed.PayAsync(advance, amount);
        return advance;
    }

    private static async Task<FinancialDocument> AddTitleAsync(
        AdvanceApplicationSeed seed, string code, FinancialDocumentNature nature = FinancialDocumentNature.Firm,
        FinancialDocumentOrigin origin = FinancialDocumentOrigin.Manual, string cardCode = FiscalReconciliationSeed.Supplier,
        FinancialDirection direction = FinancialDirection.Payable, CurrencyType currency = CurrencyType.Brl,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open, decimal amount = 3_000m)
    {
        var title = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = "PRODUTOR TESTE",
            Direction = direction, Nature = nature, Status = status, DueDate = new DateTime(2026, 12, 31),
            NetAmount = amount, OriginType = origin, BranchCode = seed.Contract.BranchCode, Currency = currency,
            PaymentTermsText = "",
        };
        seed.Context.FinancialDocuments.Add(title);
        await seed.Context.SaveChangesAsync();
        return title;
    }

    [Fact]
    public async Task Applies_free_advance_to_firm_title_of_same_partner()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var firm = await AddTitleAsync(seed, "FN-FIRM-LIVRE");

        await seed.ApplyAsync(1_200m, advance: advance, provisional: firm);

        var onFirm = await seed.LineAsync(firm.Key, FinancialSettlementOrigin.AdvanceApplication);
        var onAdvance = await seed.LineAsync(advance.Key, FinancialSettlementOrigin.AdvanceApplication);
        Assert.Equal(1_200m, onFirm.Amount);
        Assert.Equal(advance.Key, onFirm.RelatedDocumentKey);
        Assert.Equal(firm.Key, onAdvance.RelatedDocumentKey);
        Assert.Equal(1_800m, (await seed.ReloadAsync(firm.Key)).OpenAmount);
        Assert.Equal(3_800m, (await seed.ReloadAsync(advance.Key)).AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Applies_free_advance_to_firm_title_with_contract_origin()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var firm = await AddTitleAsync(seed, "FN-FIRM-2", origin: FinancialDocumentOrigin.PurchaseInvoice);

        await seed.ApplyAsync(500m, advance: advance, provisional: firm);

        Assert.Equal(2_500m, (await seed.ReloadAsync(firm.Key)).OpenAmount);
    }

    [Fact]
    public async Task Applies_free_advance_to_provisional_of_a_contract_of_same_partner()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);

        await seed.ApplyAsync(1_000m, advance: advance);

        var line = await seed.LineAsync(seed.Provisional.Key, FinancialSettlementOrigin.AdvanceApplication);
        Assert.Equal(1_000m, line.Amount);
    }

    [Fact]
    public async Task Free_advance_respects_the_target_open_amount()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var firm = await AddTitleAsync(seed, "FN-FIRM-3", amount: 300m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(400m, advance: advance, provisional: firm));

        Assert.Contains("disponível para baixa", error.Message);
    }

    [Fact]
    public async Task Free_advance_rejects_title_of_another_partner()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var other = await AddTitleAsync(seed, "FN-OUTRO", cardCode: "F0099");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(100m, advance: advance, provisional: other));

        Assert.Contains("outro parceiro", error.Message);
    }

    [Fact]
    public async Task Free_advance_partner_comparison_is_case_insensitive()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var firm = await AddTitleAsync(seed, "FN-CASE", cardCode: FiscalReconciliationSeed.Supplier.ToLowerInvariant());

        await seed.ApplyAsync(100m, advance: advance, provisional: firm);

        Assert.Equal(2_900m, (await seed.ReloadAsync(firm.Key)).OpenAmount);
    }

    [Fact]
    public async Task Free_advance_rejects_opposite_direction()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var receivable = await AddTitleAsync(seed, "FN-REC", direction: FinancialDirection.Receivable);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(100m, advance: advance, provisional: receivable));

        Assert.Contains("direção", error.Message);
    }

    [Fact]
    public async Task Free_advance_rejects_advance_target()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var target = await AddFreeAdvanceAsync(seed, code: "FN-ADT-ALVO");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(100m, advance: advance, provisional: target));

        Assert.Contains("adiantamento", error.Message);
    }

    [Fact]
    public async Task Free_advance_rejects_canceled_target()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var canceled = await AddTitleAsync(seed, "FN-CANC", status: FinancialDocumentStatus.Canceled);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(100m, advance: advance, provisional: canceled));

        Assert.Equal("Título cancelado não pode ser compensado.", error.Message);
    }

    [Fact]
    public async Task Free_advance_rejects_different_currency()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var usd = await AddTitleAsync(seed, "FN-USD", currency: CurrencyType.Usd);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(100m, advance: advance, provisional: usd));

        Assert.Contains("moedas diferentes", error.Message);
    }

    [Fact]
    public async Task Contract_advance_against_firm_title_without_contract_is_still_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var firm = await AddTitleAsync(seed, "FN-FIRM-SC");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            seed.ApplyAsync(100m, provisional: firm));

        Assert.Contains("não pertence ao contrato", error.Message);
    }

    // ---- Candidatos ----

    [Fact]
    public async Task Candidates_for_firm_title_list_free_advances_of_the_partner()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        await AddFreeAdvanceAsync(seed, code: "FN-ADT-OUTRO", cardCode: "F0099");
        await AddFreeAdvanceAsync(seed, code: "FN-ADT-REC", direction: FinancialDirection.Receivable);
        var firm = await AddTitleAsync(seed, "FN-FIRM-C", amount: 3_000m);

        var candidates = await new FinancialDocumentsGetAdvanceCandidatesService(seed.Db).ExecuteAsync(firm.Key);

        var only = Assert.Single(candidates);
        Assert.Equal(advance.Key, only.DocumentKey);
        Assert.Equal(3_000m, only.SuggestedAmount);
    }

    [Fact]
    public async Task Candidates_for_contract_provisional_union_contract_and_free_advances()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);

        var candidates = await new FinancialDocumentsGetAdvanceCandidatesService(seed.Db).ExecuteAsync(seed.Provisional.Key);

        Assert.Equal(
            new[] { seed.Advance.Key, advance.Key }.OrderBy(x => x),
            candidates.Select(c => c.DocumentKey).OrderBy(x => x));
    }

    [Fact]
    public async Task Candidates_for_advance_or_canceled_target_are_empty()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var canceled = await AddTitleAsync(seed, "FN-CANC-C", status: FinancialDocumentStatus.Canceled);
        var service = new FinancialDocumentsGetAdvanceCandidatesService(seed.Db);

        Assert.Empty(await service.ExecuteAsync(advance.Key));
        Assert.Empty(await service.ExecuteAsync(canceled.Key));
    }

    // ---- Vínculo posterior a contrato (decisão 2) ----

    private static FinancialAdvancesRelinkContractService Relink(AdvanceApplicationSeed seed) =>
        new(seed.Db, new FinancialDocumentChangeLogService(seed.Context), new FakeDocNumberSequenceService());

    [Fact]
    public async Task Relinking_partially_applied_free_advance_moves_only_the_available_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed);
        var firm = await AddTitleAsync(seed, "FN-FIRM-R");
        await seed.ApplyAsync(2_000m, advance: advance, provisional: firm);

        await Relink(seed).ExecuteAsync(advance.Key, "Purchase", seed.Contract.Key, "ana");

        var original = await seed.ReloadAsync(advance.Key);
        Assert.Null(original.PurchaseContractKey);
        Assert.Equal(0m, original.AvailableAdvanceAmount);
        var split = await seed.Context.FinancialDocuments.AsNoTracking()
            .SingleAsync(d => d.OriginKey == advance.Key && d.Nature == FinancialDocumentNature.Advance);
        Assert.Equal(seed.Contract.Key, split.PurchaseContractKey);
        Assert.Equal(3_000m, split.NetAmount);
    }

    [Fact]
    public async Task Relinking_fully_applied_free_advance_is_refused()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        var advance = await AddFreeAdvanceAsync(seed, amount: 1_000m);
        var firm = await AddTitleAsync(seed, "FN-FIRM-F");
        await seed.ApplyAsync(1_000m, advance: advance, provisional: firm);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Relink(seed).ExecuteAsync(advance.Key, "Purchase", seed.Contract.Key, "ana"));

        Assert.Contains("crédito disponível", error.Message);
    }
}
