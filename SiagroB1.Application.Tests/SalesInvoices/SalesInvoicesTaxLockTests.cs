using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// A trava da tributação STANDALONE na edição do documento de saída. Pendente: o cálculo
/// sobrescreve o que vier no corpo (o PATCH reenvia a entidade inteira). Confirmado (a Conferência
/// de entregas usa o mesmo serviço): os tributos voltam ao gravado. Regra inativa: nada muda.
/// </summary>
public class SalesInvoicesTaxLockTests
{
    private const string CardBa = "C-BA";
    private const string CardSp = "C-SP";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA", [CardSp] = "CLIENTE SP" },
        states: new() { [CardBa] = "BA", [CardSp] = "SP" });

    private static FakeItemService Items() =>
        new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS", ["MILHO"] = "MILHO EM GRAOS" });

    /// <summary>Base com filial, produtos, alíquota, natureza padrão e um documento Pendente de uma linha.</summary>
    private static async Task<(UnitOfWork db, SalesInvoice invoice, SalesInvoiceItem line)> Seed(bool issuesNfe = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = 0, Ncm = "12019000" });
        db.Context.Items.Add(new Item { ItemCode = "MILHO", ItemName = "MILHO", GoodsOrigin = 0, Ncm = null });
        db.Context.IbsCbsRates.Add(new IbsCbsRate
        {
            StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m,
        });
        await db.SaveChangesAsync();

        await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda de grãos", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsOutStateCst = "00",
            PisCst = "01", PisRate = 1.65m, CofinsCst = "01", CofinsRate = 7.6m,
            ExcludeIcmsFromPisCofinsBase = true,
            RequiresQuantity = true, IsDefault = true,
        });

        var line = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 30000m, UnitPrice = 2m,
        };

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = CardBa, InvoiceStatus = InvoiceStatus.Pending,
            InvoiceDate = new DateTime(2026, 10, 1), Items = [line],
        };

        await TaxTestServices.Apply(db, Partners()).ApplyAsync(invoice, invoice.Items);

        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice, line);
    }

    private static SalesInvoicesItemsCreateService ItemsCreate(UnitOfWork db) =>
        new(db, Items(), TaxTestServices.Apply(db, Partners()), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesItemsCreateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemsUpdate(UnitOfWork db) =>
        new(db, Items(),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.Apply(db, Partners()), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, Partners(), TaxTestServices.Apply(db, Partners()), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Adding_a_line_with_rule_active_calculates_it()
    {
        var (db, invoice, _) = await Seed();
        var added = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = 1000m, UnitPrice = 2m,
        };

        await ItemsCreate(db).ExecuteAsync(added, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == added.Key);
        Assert.Equal("6102", stored.Cfop);
        Assert.Equal(140.00m, stored.IcmsValue);
        Assert.NotNull(stored.UsageCode);
    }

    [Fact]
    public async Task Adding_a_line_with_missing_setup_returns_a_business_error()
    {
        var (db, invoice, _) = await Seed();
        var added = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "MILHO", UnitOfMeasureCode = "KG",
            Quantity = 1000m, UnitPrice = 2m,
        };

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemsCreate(db).ExecuteAsync(added, "tester"));
        Assert.Contains("sem NCM", ex.Message);
    }

    [Fact]
    public async Task Patch_that_resends_old_taxes_is_overwritten_by_the_calculation()
    {
        var (db, _, line) = await Seed();

        // Como o Delta do PATCH: a entidade rastreada chega mutada, com o tributo antigo/forjado.
        line.Quantity = 15000m;
        line.IcmsValue = 999m;

        await ItemsUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(2100.00m, stored.IcmsValue);
        Assert.Equal(15000m, stored.Quantity);
    }

    [Fact]
    public async Task Confirmed_invoice_line_keeps_its_taxes_on_reconciliation_edit()
    {
        var (db, invoice, line) = await Seed();
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        await db.SaveChangesAsync();

        line.DeliveredQuantity = 29000m;
        line.IcmsValue = 999m;
        line.Cfop = "9999";

        await ItemsUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(4200.00m, stored.IcmsValue);
        Assert.Equal("6102", stored.Cfop);
        Assert.Equal(29000m, stored.DeliveredQuantity);
    }

    [Fact]
    public async Task Changing_the_customer_on_the_header_recalculates_every_line()
    {
        var (db, invoice, line) = await Seed();

        invoice.CardCode = CardSp;
        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal("5102", stored.Cfop);
        Assert.Equal("51", stored.CstIcms);
        Assert.Equal(10800.00m, stored.IcmsDeferredValue);
    }

    [Fact]
    public async Task Header_change_with_rule_inactive_keeps_typed_values()
    {
        var (db, invoice, line) = await Seed(issuesNfe: false);
        line.IcmsValue = 77m;
        await db.SaveChangesAsync();

        invoice.CardCode = CardSp;
        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(77m, stored.IcmsValue);
        Assert.Null(stored.Cfop);
    }

    [Fact]
    public async Task Line_update_with_rule_inactive_keeps_typed_values()
    {
        var (db, _, line) = await Seed(issuesNfe: false);

        line.IcmsValue = 77m;
        await ItemsUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal(77m, stored.IcmsValue);
    }

    /// <summary>
    /// Pendente com NF-e autorizada (a confirmação falhou ou foi estornada): uma edição que não é
    /// fiscal, depois de a natureza mudar, não pode recalcular a linha e descolá-la do XML.
    /// </summary>
    [Fact]
    public async Task Pending_line_with_authorized_nfe_keeps_its_taxes_after_the_setup_changes()
    {
        var (db, invoice, line) = await Seed();
        invoice.NfeStatus = NfeStatus.Authorized;
        await db.SaveChangesAsync();

        var usage = await db.Context.Usages.SingleAsync();
        usage.CfopOutgoingOutState = "6999";
        usage.IcmsOutStateCst = "20";
        await db.SaveChangesAsync();

        line.SalesContractKey = Guid.NewGuid();
        await ItemsUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal("6102", stored.Cfop);
        Assert.Equal("00", stored.CstIcms);
        Assert.Equal(4200.00m, stored.IcmsValue);
        Assert.NotNull(stored.SalesContractKey);
    }

    /// <summary>Pendente com NF-e cancelada (o cancelamento local falhou): mesma trava da autorizada.</summary>
    [Fact]
    public async Task Pending_line_with_cancelled_nfe_keeps_its_taxes_after_the_setup_changes()
    {
        var (db, invoice, line) = await Seed();
        invoice.NfeStatus = NfeStatus.Cancelled;
        await db.SaveChangesAsync();

        var usage = await db.Context.Usages.SingleAsync();
        usage.CfopOutgoingOutState = "6999";
        usage.IcmsOutStateCst = "20";
        await db.SaveChangesAsync();

        line.SalesContractKey = Guid.NewGuid();
        await ItemsUpdate(db).ExecuteAsync(line.Key!.Value, line, "tester");

        var stored = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == line.Key);
        Assert.Equal("6102", stored.Cfop);
        Assert.Equal("00", stored.CstIcms);
        Assert.Equal(4200.00m, stored.IcmsValue);
    }
}
