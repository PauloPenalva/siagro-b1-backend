using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Revisão final (C1): o firme não fica congelado no momento do vínculo. Editar a linha conciliada (valor, Funrural,
/// contrato, natureza) ou o cabeçalho (tipo, emitente) passa pelo PurchaseFiscalReconciliationCleanupInterceptor —
/// todo caminho de gravação: firme solto recalculado com log, não-solto recusa, contrato/tipo/emitente recusam sempre.
/// </summary>
public class FinancialDocumentsFirmLineEditTests
{
    private const string Composed =
        "O título ST-0001 do vínculo já está composto no título FN-PROV-1: desfaça a composição antes de alterar a conciliação.";

    private const string ContractChange =
        "Linha conciliada a romaneio: desfaça a conciliação antes de trocar o contrato.";

    private const string HeaderChange =
        "Documento conciliado a romaneio: desfaça a conciliação antes de alterar o tipo ou o emitente.";

    private static Task<FirmPayableSeed> SeedAsync(bool isStandalone = true) =>
        FirmPayableSeed.CreateAsync(
            interceptor: new PurchaseFiscalReconciliationCleanupInterceptor(isStandalone),
            erp: isStandalone ? "STANDALONE" : "SAPB1");

    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    [Fact]
    public async Task Price_change_on_a_linked_line_recalculates_the_loose_firm_and_logs_it()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();

        c.Line.UnitPrice = 3m; // 90.000,00 − 815,00 Funrural
        await seed.Db.SaveChangesAsync();

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(29_728.33m, firm.NetAmount); // 10.000 × 89.185,00 ÷ 30.000
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        var log = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(c.Firm.Key, log.FinancialDocumentKey);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("19.728,33", log.OldValue);
        Assert.Equal("29.728,33", log.NewValue);
        Assert.Equal("Sistema", log.ChangedBy);
    }

    [Fact]
    public async Task Funrural_and_charges_change_recalculates_the_loose_firm_with_the_document_user()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();

        c.Line.FunruralInssValue = 0m;
        c.Line.FreightValue = 300m; // 60.300,00 sem Funrural
        c.Invoice.UpdatedBy = "carla";
        await seed.Db.SaveChangesAsync();

        Assert.Equal(20_100m, (await seed.ReloadAsync(c.Firm!.Key)).NetAmount);
        Assert.Equal("carla", (await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().SingleAsync()).ChangedBy);
    }

    [Fact]
    public async Task Value_change_on_a_composed_firm_is_refused()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        c.Line.UnitPrice = 3m;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(Composed, error.Message);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, (await seed.ReloadAsync(c.Firm.Key)).NetAmount);
    }

    [Fact]
    public async Task Value_change_on_a_firm_with_dation_is_refused()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();
        seed.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = c.Firm!.Key, Amount = 100m, SettlementDate = DateTime.Today,
            Origin = FinancialSettlementOrigin.Dation, Notes = "sobra",
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(seed.Context, c.Firm.Key);
        await seed.Context.SaveChangesAsync();

        c.Line.DiscountValue = 100m;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(
            "O título ST-0001 do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.",
            error.Message);
    }

    [Fact]
    public async Task Edit_that_does_not_change_the_value_keeps_a_composed_firm_untouched()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        c.Line.ItemName = "SOJA";
        await seed.Db.SaveChangesAsync();

        Assert.Empty(seed.Context.FinancialDocumentChangeLogs.AsNoTracking()
            .Where(l => l.Field == FinancialDocumentChangeLogFields.NetAmount));
    }

    [Fact]
    public async Task Contract_change_on_a_linked_line_is_refused()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();
        var other = await seed.Fiscal.AddContractAsync("PC-2");

        c.Line.PurchaseContractKey = other.Key;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(ContractChange, error.Message);
    }

    [Fact]
    public async Task Contract_change_is_refused_even_without_a_firm()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync(createsFinancialDocument: false);
        var other = await seed.Fiscal.AddContractAsync("PC-2");

        c.Line.PurchaseContractKey = other.Key;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(ContractChange, error.Message);
    }

    [Fact]
    public async Task Nature_without_financial_cancels_the_loose_firm()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();

        c.Line.UsageCode = seed.NonFinancialUsageCode;
        await seed.Db.SaveChangesAsync();

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Natureza não gera financeiro", firm.CancellationReason);
        Assert.Equal("Sistema", firm.CanceledBy);
        Assert.Single(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Removing_the_nature_cancels_the_loose_firm()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();

        c.Line.UsageCode = null;
        await seed.Db.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Nature_without_financial_on_a_composed_firm_is_refused()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        c.Line.UsageCode = seed.NonFinancialUsageCode;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(Composed, error.Message);
    }

    [Fact]
    public async Task Nature_that_still_creates_financial_keeps_the_firm()
    {
        var seed = await SeedAsync(isStandalone: false); // SAPB1: o OUSG não tem a flag — conta como gera
        var c = await seed.ConciliateAsync(funrural: 0m);

        c.Line.UsageCode = seed.NonFinancialUsageCode;
        await seed.Db.SaveChangesAsync();

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal(20_000m, firm.NetAmount);
    }

    [Fact]
    public async Task Issuer_change_on_a_linked_document_is_refused()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();

        c.Invoice.CardCode = "F0002";
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(HeaderChange, error.Message);
    }

    [Fact]
    public async Task Type_change_on_a_linked_document_is_refused()
    {
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync();

        c.Invoice.InvoiceType = PurchaseInvoiceType.Return;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(HeaderChange, error.Message);
    }

    [Fact]
    public async Task Issuer_change_on_a_document_without_links_is_accepted()
    {
        var seed = await SeedAsync();
        var (invoice, _) = await seed.Fiscal.AddInvoiceAsync(30_000m, contract: seed.Contract);

        invoice.CardCode = "F0002";
        await seed.Db.SaveChangesAsync();

        Assert.Equal("F0002", (await seed.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).CardCode);
    }
    [Fact]
    public async Task Nature_turned_financial_creates_no_firm_until_the_link_is_saved_again()
    {
        // O interceptor não numera título (DocNumberSequenceService); regravar o par pela tela ("Vincular" sugere o par
        // existente com a quantidade atual) faz o sync criar o firme.
        var seed = await SeedAsync();
        var c = await seed.ConciliateAsync(createsFinancialDocument: false);

        c.Line.UsageCode = seed.FinancialUsageCode;
        await seed.Db.SaveChangesAsync();
        Assert.Empty(seed.Context.FinancialDocuments.AsNoTracking().Where(d => d.OriginKey == c.Link.Key));

        await seed.SaveLinkAsync(c, 10_000m);

        var firm = await seed.Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.OriginKey == c.Link.Key);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, firm.NetAmount);
    }
}
