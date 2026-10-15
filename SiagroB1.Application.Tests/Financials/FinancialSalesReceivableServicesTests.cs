using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15, regras 12–14: o financeiro dos firmes de compra vale para os firmes a receber.</summary>
public class FinancialSalesReceivableServicesTests
{
    private static FinancialDocumentsComposeService Compose(SalesReceivableDocs d) =>
        new(d.Db, new FinancialDocumentChangeLogService(d.Context));

    [Fact]
    public async Task Sales_firm_is_composed_into_the_sales_provisional_and_received_there()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(9_000m);

        await Compose(d).ExecuteAsync(d.Provisional.Key, [firm.Key], "ana");
        await new FinancialDocumentsSettleService(d.Db)
            .ExecuteAsync(d.Provisional.Key, "CX01", 9_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        var provisional = await d.ReloadAsync(d.Provisional.Key);
        Assert.Equal(9_000m, provisional.ComposedAmount);
        Assert.Equal(9_000m, provisional.SettledAmount);
        Assert.Equal(0m, (await d.ReloadAsync(firm.Key)).OpenAmount);
    }

    [Fact]
    public async Task Sales_return_enters_the_composition_negative()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(9_000m);
        var refund = await d.ReturnAsync(1_000m);

        await Compose(d).ExecuteAsync(d.Provisional.Key, [refund.Key, firm.Key], "ana");

        Assert.Equal(8_000m, (await d.ReloadAsync(d.Provisional.Key)).ComposedAmount);
    }

    [Fact]
    public async Task Purchase_firm_does_not_compose_into_a_sales_provisional()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var purchaseFirm = await d.AddAsync(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseInvoice, 500m,
            purchaseContractKey: Guid.NewGuid(), direction: FinancialDirection.Payable);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(d).ExecuteAsync(d.Provisional.Key, [purchaseFirm.Key], "ana"));

        Assert.Equal($"O título {purchaseFirm.Code} é de outro contrato.", error.Message);
    }

    [Fact]
    public async Task Sales_firm_of_another_contract_does_not_compose()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var other = await d.FirmAsync(500m, contractKey: Guid.NewGuid());

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(d).ExecuteAsync(d.Provisional.Key, [other.Key], "ana"));

        Assert.Equal($"O título {other.Code} é de outro contrato.", error.Message);
    }

    [Fact]
    public async Task Sales_advance_is_not_composable()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(d).ExecuteAsync(d.Provisional.Key, [advance.Key], "ana"));

        Assert.Equal($"O título {advance.Code} não é firme nem devolução de documento de saída.", error.Message);
    }

    [Fact]
    public async Task Sales_firm_cannot_be_settled_in_cash()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() => new FinancialDocumentsSettleService(d.Db)
            .ExecuteAsync(firm.Key, "CX01", 500m, DateTime.Today, 0m, 0m, 0m, null, null, "ana"));

        Assert.Equal("Título firme não recebe baixa direta: componha no provisório ou baixe por dação.", error.Message);
    }

    [Fact]
    public async Task Dation_settles_the_sales_firm_and_refuses_other_sales_documents()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(500m);
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m);
        var dation = new FinancialDocumentsDationService(d.Db);

        await dation.ExecuteAsync(firm.Key, 500m, DateTime.Today, "sobra faturada acima do contrato", "ana");
        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            dation.ExecuteAsync(advance.Key, 100m, DateTime.Today, "x", "ana"));

        Assert.Equal(FinancialDocumentStatus.Settled, (await d.ReloadAsync(firm.Key)).Status);
        Assert.Equal("Dação só se aplica a título firme do documento de saída.", error.Message);
    }

    [Fact]
    public async Task Manual_netting_pairs_the_sales_return_with_a_loose_firm_of_the_same_contract()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(900m);
        var refund = await d.ReturnAsync(300m);

        await new FinancialDocumentsNettingService(d.Db).ExecuteAsync(refund.Key, firm.Key, "ana");

        Assert.Equal(300m, (await d.ReloadAsync(refund.Key)).SettledAmount);
        Assert.Equal(300m, (await d.ReloadAsync(firm.Key)).SettledAmount);
        Assert.Equal(2, await d.Context.FinancialSettlements.CountAsync(s => s.Origin == FinancialSettlementOrigin.Netting));
    }

    [Fact]
    public async Task Manual_netting_refuses_a_sales_document_that_is_not_a_firm()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var refund = await d.ReturnAsync(300m);
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsNettingService(d.Db).ExecuteAsync(refund.Key, advance.Key, "ana"));

        Assert.Equal($"O título {advance.Code} não é um título firme do documento de saída.", error.Message);
    }

    [Fact]
    public async Task Auto_netting_for_sales_pairs_the_return_with_the_loose_origin_line_firm_of_the_same_contract()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var originLine = Guid.NewGuid();
        var firm = await d.FirmAsync(900m, originKey: originLine);
        var otherContractFirm = await d.FirmAsync(900m, contractKey: Guid.NewGuid(), originKey: originLine);
        var refund = await d.ReturnAsync(300m);

        await new FinancialDocumentsNettingService(d.Db).EnqueueAutoForSalesAsync(refund, originLine, "ana");
        await d.Db.SaveChangesAsync();

        Assert.Equal(300m, (await d.ReloadAsync(firm.Key)).SettledAmount);
        Assert.Equal(0m, (await d.ReloadAsync(otherContractFirm.Key)).SettledAmount);
        Assert.Equal(300m, (await d.ReloadAsync(refund.Key)).SettledAmount);
    }

    [Fact]
    public async Task Composition_candidates_of_the_sales_provisional_are_its_sales_firms_and_returns()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(900m);
        var refund = await d.ReturnAsync(100m);
        await d.FirmAsync(900m, contractKey: Guid.NewGuid());
        await d.AddAsync(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, 50m);

        var result = await new FinancialDocumentsGetCompositionService(d.Db).ExecuteAsync(d.Provisional.Key);

        Assert.Equal(new[] { firm.Key, refund.Key }.OrderBy(k => k), result.Candidates.Select(c => c.DocumentKey).OrderBy(k => k));
    }

    [Fact]
    public async Task Sales_advance_is_applied_to_the_sales_provisional()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(9_000m);
        await Compose(d).ExecuteAsync(d.Provisional.Key, [firm.Key], "ana");
        var advance = await d.AddAsync(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 2_000m);
        await new FinancialDocumentsSettleService(d.Db)
            .ExecuteAsync(advance.Key, "CX01", 2_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        var candidates = await new FinancialDocumentsGetAdvanceCandidatesService(d.Db).ExecuteAsync(d.Provisional.Key);
        await new FinancialDocumentsApplyAdvanceService(d.Db, new FinancialDocumentChangeLogService(d.Context))
            .ExecuteAsync(d.Provisional.Key, advance.Key, 2_000m, DateTime.Today, "ana");

        Assert.Equal(advance.Key, Assert.Single(candidates).DocumentKey);
        Assert.Equal(2_000m, (await d.ReloadAsync(d.Provisional.Key)).SettledAmount);
        Assert.Equal(2_000m, (await d.ReloadAsync(advance.Key)).AppliedAmount);
    }

    [Fact]
    public async Task Sales_firm_cannot_be_cancelled_on_the_financial_screen()
    {
        var d = await SalesReceivableDocs.CreateAsync();
        var firm = await d.FirmAsync(500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(d.Db).ExecuteAsync(firm.Key, "teste", "ana"));

        Assert.Equal(
            "Título firme do documento de saída nasce da confirmação: cancele ou estorne o documento de saída para cancelá-lo.",
            error.Message);
    }
}
