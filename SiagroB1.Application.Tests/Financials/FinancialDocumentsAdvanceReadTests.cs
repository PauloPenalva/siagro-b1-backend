using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-14: candidatos do diálogo "Compensar adiantamento" e aplicado/crédito no financeiro do contrato.</summary>
public class FinancialDocumentsAdvanceReadTests
{
    private static FinancialDocumentsGetAdvanceCandidatesService Candidates(AdvanceApplicationSeed s) => new(s.Db);

    [Fact]
    public async Task Candidates_are_the_contract_advances_with_credit_in_the_same_currency()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.AddAdvanceAsync(seed.Contract, 30_000m, 30_000m, "FN-ADT-2");
        await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-3");
        await seed.AddAdvanceAsync(seed.Contract, 5_000m, 0m, "FN-ADT-4", CurrencyType.Usd);
        var other = await seed.Firm.Fiscal.AddContractAsync("PC-2");
        await seed.AddAdvanceAsync(other, 5_000m, 5_000m, "FN-ADT-5");

        var list = await Candidates(seed).ExecuteAsync(seed.Provisional.Key);

        Assert.Equal(new[] { "FN-ADT-1", "FN-ADT-2" }, list.Select(c => c.Code!).Order().ToArray());
        Assert.Equal(10_000m, list.Single(c => c.Code == "FN-ADT-1").SuggestedAmount);
        Assert.Equal(19_728.33m, list.Single(c => c.Code == "FN-ADT-2").SuggestedAmount);
    }

    [Fact]
    public async Task Candidate_shows_paid_applied_and_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        var candidate = (await Candidates(seed).ExecuteAsync(seed.Provisional.Key)).Single();

        Assert.Equal(seed.Advance.Key, candidate.DocumentKey);
        Assert.Equal(10_000m, candidate.SettledAmount);
        Assert.Equal(6_000m, candidate.AppliedAmount);
        Assert.Equal(4_000m, candidate.AvailableAdvanceAmount);
        Assert.Equal(4_000m, candidate.SuggestedAmount);
    }

    [Fact]
    public async Task A_key_that_is_not_a_purchase_provisional_has_no_candidates()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();

        Assert.Empty(await Candidates(seed).ExecuteAsync(seed.Advance.Key));
    }

    [Fact]
    public async Task By_contract_rows_carry_applied_and_credit()
    {
        var seed = await AdvanceApplicationSeed.CreateAsync();
        await seed.ApplyAsync(6_000m);

        var rows = (await new FinancialDocumentsGetByContractService(seed.Db)
            .ExecuteAsync("Purchase", seed.Contract.Key)).ToList();

        var advance = rows.Single(r => r.Code == "FN-ADT-1");
        Assert.Equal(6_000m, advance.AppliedAmount);
        Assert.Equal(4_000m, advance.AvailableAdvanceAmount);
        Assert.Equal(0m, rows.Single(r => r.Code == "FN-PROV-1").AvailableAdvanceAmount);
    }
}
