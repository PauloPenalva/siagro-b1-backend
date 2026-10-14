using System.Globalization;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Regra 15: o provisório baixa só o composto; firme e devolução da conciliação não recebem baixa em dinheiro.</summary>
public class FinancialDocumentsSettlementGuardFirmTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsSettleService Settle() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, decimal net, decimal composed = 0m,
        FinancialDirection direction = FinancialDirection.Payable)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN000001", CardCode = "F0001", Direction = direction, Nature = nature,
            OriginType = origin, OriginKey = Guid.NewGuid(), NetAmount = net, DueDate = new DateTime(2026, 12, 31),
            Currency = CurrencyType.Brl,
        };
        _db.Context.FinancialDocuments.Add(document);
        if (composed != 0m)
            _db.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
            {
                Key = Guid.NewGuid(), ProvisionalDocumentKey = document.Key, ComposedDocumentKey = Guid.NewGuid(), Amount = composed,
            });
        await _db.Context.SaveChangesAsync();
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private static string AboveAvailable(decimal available) =>
        $"Valor acima do disponível para baixa ({available.ToString("N2", PtBr)}): componha mais títulos firmes antes.";

    [Fact]
    public async Task Provisional_without_composition_has_nothing_available()
    {
        var provisional = await SeedAsync(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, 1000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Settle().ExecuteAsync(provisional.Key, "CX01", 100m, DateTime.Today, 0m, 0m, 0m, null, null, "tester"));

        Assert.Equal(AboveAvailable(0m), error.Message);
    }

    [Fact]
    public async Task Provisional_settles_up_to_the_composed_amount()
    {
        var provisional = await SeedAsync(
            FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, 1000m, composed: 400m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Settle().ExecuteAsync(provisional.Key, "CX01", 400.01m, DateTime.Today, 0m, 0m, 0m, null, null, "tester"));
        Assert.Equal(AboveAvailable(400m), error.Message);

        await Settle().ExecuteAsync(provisional.Key, "CX01", 400m, DateTime.Today, 0m, 0m, 0m, null, null, "tester");

        Assert.Equal(400m, provisional.SettledAmount);
        Assert.Equal(0m, provisional.AvailableToSettleAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);
    }

    [Theory]
    [InlineData(FinancialDocumentNature.Firm)]
    [InlineData(FinancialDocumentNature.PurchaseReturn)]
    public async Task Conciliation_firm_refuses_cash_settlement(FinancialDocumentNature nature)
    {
        var firm = await SeedAsync(nature, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Settle().ExecuteAsync(firm.Key, "CX01", 100m, DateTime.Today, 0m, 0m, 0m, null, null, "tester"));

        Assert.Equal("Título firme não recebe baixa direta: componha no provisório ou baixe por dação.", error.Message);
    }

    [Fact]
    public async Task Washout_receivable_still_settles_in_cash()
    {
        var receivable = await SeedAsync(
            FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, 500m,
            direction: FinancialDirection.Receivable);

        await Settle().ExecuteAsync(receivable.Key, "CX01", 500m, DateTime.Today, 0m, 0m, 0m, null, null, "tester");

        Assert.Equal(FinancialDocumentStatus.Settled, receivable.Status);
    }
}
