using System.Text.Json;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Leituras dos títulos firmes (spec 2026-10-13): Detail do título, seção Financeiro do contrato e conciliação.</summary>
public class FinancialDocumentsReadModelTests
{
    private static FinancialDocumentsGetCompositionService Composition(FirmPayableSeed s) => new(s.Db);

    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    [Fact]
    public async Task Provisional_shows_its_lines_and_the_composable_candidates()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 25_000m);
        var c1 = await seed.ConciliateAsync(taxNumber: "1001");
        var c2 = await seed.ConciliateAsync(taxNumber: "1002");
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: "2001");
        var other = await seed.Fiscal.AddContractAsync("PC-2");
        await seed.ConciliateAsync(contract: other, taxNumber: "3001");
        await ComposeAsync(seed, c1.Firm!.Key);

        var dto = await Composition(seed).ExecuteAsync(seed.Provisional.Key);

        var line = Assert.Single(dto.Lines);
        Assert.Equal(c1.Firm.Key, line.DocumentKey);
        Assert.Equal("ST-0001", line.DocumentCode);
        Assert.Equal("FN-PROV-1", line.ProvisionalCode);
        Assert.Equal(19_728.33m, line.Amount);
        Assert.Equal("1001-1", line.TaxDocumentNumber);
        Assert.Equal(c1.Transaction.Code, line.StorageTransactionCode);
        Assert.Equal("Firm", line.Nature);

        Assert.Equal(2, dto.Candidates.Count);
        Assert.Equal(5_271.67m, dto.Candidates.Single(x => x.DocumentKey == c2.Firm!.Key).SuggestedAmount);
        var refund = dto.Candidates.Single(x => x.DocumentKey == r.Firm!.Key);
        Assert.Equal("PurchaseReturn", refund.Nature);
        Assert.Equal(-2_959.25m, refund.SuggestedAmount);
    }

    [Fact]
    public async Task Firm_shows_where_it_is_composed()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        var dto = await Composition(seed).ExecuteAsync(c.Firm.Key);

        Assert.Equal(seed.Provisional.Key, Assert.Single(dto.Lines).ProvisionalDocumentKey);
        Assert.Equal("FN-PROV-1", dto.Lines[0].ProvisionalCode);
        Assert.Empty(dto.Candidates);
    }

    [Fact]
    public async Task Return_lists_only_loose_firms_as_netting_candidates()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m);
        var composed = await seed.ConciliateAsync(taxNumber: "1001");
        var loose = await seed.ConciliateAsync(taxNumber: "1002");
        await ComposeAsync(seed, composed.Firm!.Key);
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: "2001");

        var dto = await Composition(seed).ExecuteAsync(r.Firm!.Key);

        var candidate = Assert.Single(dto.Candidates);
        Assert.Equal(loose.Firm!.Key, candidate.DocumentKey);
        Assert.Equal(2_959.25m, candidate.SuggestedAmount);
    }

    [Fact]
    public async Task Contract_financial_section_brings_composed_invoice_and_transaction()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        var rows = (await new FinancialDocumentsGetByContractService(seed.Db).ExecuteAsync("Purchase", seed.Contract.Key)).ToList();

        Assert.Equal(19_728.33m, rows.Single(x => x.Key == seed.Provisional.Key).ComposedAmount);
        var firm = rows.Single(x => x.Key == c.Firm.Key);
        Assert.Equal("Firm", firm.Nature);
        Assert.Equal("1001-1", firm.TaxDocumentNumber);
        Assert.Equal(c.Transaction.Code, firm.StorageTransactionCode);
    }

    [Fact]
    public async Task Contract_financial_endpoint_accepts_the_quoted_contract_type_sent_by_the_odata_route()
    {
        // A rota literal entrega ContractType='Purchase' COM aspas; o controller as remove.
        var seed = await FirmPayableSeed.CreateAsync();
        await seed.ConciliateAsync();
        var controller = new SiagroB1.Web.Functions.Financials.FinancialDocumentsGetByContractController(
            new FinancialDocumentsGetByContractService(seed.Db));

        var result = await controller.Get("'Purchase'", seed.Contract.Key);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result.Result);
        Assert.NotEmpty(Assert.IsAssignableFrom<IEnumerable<FinancialDocumentByContractDto>>(ok.Value));
    }

    [Fact]
    public async Task Conciliation_link_shows_the_firm_and_its_situation()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var loose = Assert.Single(await seed.Reconciliation().ListLinksAsync(c.Transaction.Key));
        Assert.Equal(c.Firm!.Key, loose.FirmDocumentKey);
        Assert.Equal("ST-0001", loose.FirmDocumentCode);
        Assert.Equal("Solto", loose.FirmSituation);

        await ComposeAsync(seed, c.Firm.Key);
        Assert.Equal("Composto", Assert.Single(await seed.Reconciliation().ListLinksAsync(c.Transaction.Key)).FirmSituation);

        var row = Assert.Single(seed.Context.FinancialDocumentCompositions);
        await new FinancialDocumentsUncomposeService(seed.Db).ExecuteAsync(row.Key, "ana");
        c.Line.UsageCode = seed.NonFinancialUsageCode;       // natureza trocada por uma que não gera financeiro (D10)
        await seed.Db.SaveChangesAsync();
        await seed.SaveLinkAsync(c, 10_000m);
        Assert.Equal("Cancelado", Assert.Single(await seed.Reconciliation().ListLinksAsync(c.Transaction.Key)).FirmSituation);
    }

    [Fact]
    public void New_dtos_keep_pascal_case_names()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        object[] dtos =
        [
            new FinancialDocumentCompositionDto { Lines = [new()], Candidates = [new()] },
            new FinancialDocumentCompositionLineDto(),
            new FinancialDocumentCompositionCandidateDto(),
            new FinancialDocumentByContractDto(),
        ];

        foreach (var dto in dtos)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, dto.GetType(), web));
            Assert.Equal(dto.GetType().GetProperties().Select(p => p.Name).Order().ToList(),
                doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
        }
    }
}
