using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// A criação do documento de saída com a tributação STANDALONE ativa: calcula as linhas antes de
/// numerar e torna o faturamento de romaneio ESTRITO — a tolerância de CFOP existe para bases
/// sem cadastro fiscal, e uma filial que emite NF-e não pode faturar com CFOP em branco.
/// </summary>
public class SalesInvoicesCreateTaxationTests
{
    private const string CardBa = "C-BA";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA" },
        states: new() { [CardBa] = "BA" });

    private static async Task<UnitOfWork> Seed(bool issuesNfe = true, bool outStateCfop = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = 0, Ncm = "12019000" });
        db.Context.IbsCbsRates.Add(new IbsCbsRate
        {
            StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m,
        });
        await db.SaveChangesAsync();

        await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda de grãos", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = outStateCfop ? "6102" : null,
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsOutStateCst = "00",
            PisCst = "01", PisRate = 1.65m, CofinsCst = "01", CofinsRate = 7.6m,
            ExcludeIcmsFromPisCofinsBase = true,
            IbsCbsCst = "000", IbsCbsClassCode = "000001",
            RequiresQuantity = true, IsDefault = true,
        });

        return db;
    }

    private static SalesInvoicesCreateService Create(UnitOfWork db)
    {
        var partners = Partners();
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesCreateService(
            db,
            partners,
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.Apply(db, partners),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static SalesInvoice Invoice(SalesInvoiceType type = SalesInvoiceType.Normal) => new()
    {
        Key = Guid.NewGuid(),
        BranchCode = "01",
        CardCode = CardBa,
        InvoiceType = type,
        InvoiceDate = new DateTime(2026, 10, 1),
        GrossWeight = 30000m,
        NetWeight = 30000m,
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG",
                Quantity = 30000m, UnitPrice = 2m, SalesContractKey = Guid.NewGuid(),
            },
        ],
    };

    /// <summary>O romaneio precisa existir: o serviço o relê para gravar o vínculo.</summary>
    private static async Task AttachTransactionAsync(UnitOfWork db, SalesInvoice invoice)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "ROM-0001", CardCode = CardBa, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "01", GrossWeight = 30000m, NetWeight = 30000m,
        };

        db.Context.StorageTransactions.Add(transaction);
        await db.SaveChangesAsync();

        invoice.SalesTransactions.Add(transaction);
    }

    [Fact]
    public async Task Standalone_invoice_with_rule_active_is_created_with_the_tax_snapshot()
    {
        var db = await Seed();
        var invoice = Invoice();

        await Create(db).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("6102", item.Cfop);
        Assert.Equal(4200.00m, item.IcmsValue);
        Assert.Equal(455.75m, item.CbsValue);
    }

    /// <summary>
    /// A tela de inclusão deixa escolher o Status: um corpo com "Confirmado" não pode fazer o
    /// documento nascer sem imposto e sem CFOP — o serviço força Pendente de qualquer jeito.
    /// </summary>
    [Fact]
    public async Task Create_with_non_pending_status_in_the_body_still_calculates()
    {
        var db = await Seed();
        var invoice = Invoice();
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;

        await Create(db).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("6102", item.Cfop);
        Assert.Equal(4200.00m, item.IcmsValue);
    }

    [Fact]
    public async Task Shipment_billing_with_rule_active_fails_without_cfop()
    {
        var db = await Seed(outStateCfop: false);
        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(invoice, "tester"));

        Assert.Contains("sem CFOP de saída fora do estado", ex.Message);
    }

    [Fact]
    public async Task Shipment_billing_with_rule_active_calculates_the_taxes()
    {
        var db = await Seed();
        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        await Create(db).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();
        Assert.NotNull(item.UsageCode);
        Assert.Equal(4200.00m, item.IcmsValue);
    }

    [Fact]
    public async Task Shipment_billing_with_rule_inactive_keeps_the_tolerance()
    {
        var db = await Seed(issuesNfe: false, outStateCfop: false);
        var invoice = Invoice();
        await AttachTransactionAsync(db, invoice);

        await Create(db).ExecuteAsync(invoice, "tester");

        var item = invoice.Items.Single();
        Assert.Null(item.Cfop);
        Assert.Equal(0m, item.IcmsValue);
    }

    [Fact]
    public async Task Return_invoice_with_rule_active_is_not_calculated()
    {
        var db = await Seed();
        var invoice = Invoice(SalesInvoiceType.Return);
        invoice.Items.Single().IcmsValue = 5m;

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(5m, invoice.Items.Single().IcmsValue);
    }

    [Fact]
    public async Task Normal_document_keeps_the_document_kind_from_the_body()
    {
        var db = await Seed();
        var invoice = Invoice();
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(TaxDocumentKind.Other,
            (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).TaxDocumentKind);
    }

    /// <summary>O tipo só vale para o Normal: a devolução própria é NF-e e a do cliente não usa o campo.</summary>
    [Fact]
    public async Task Return_document_is_always_nfe()
    {
        var db = await Seed();
        var invoice = Invoice(SalesInvoiceType.Return);
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(TaxDocumentKind.Nfe, invoice.TaxDocumentKind);
    }
}
