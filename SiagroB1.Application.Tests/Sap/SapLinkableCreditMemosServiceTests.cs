using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// Regras da listagem "Vincular NF de devolução" (GAC-1226): A/R Credit Memo do SAP para um documento de
/// devolução da carga. SAP e Siagro são dois bancos: o InMemory esconde LINQ intraduzível, por isso há um teste
/// de <c>ToQueryString</c> no provider SqlServer.
/// </summary>
public class SapLinkableCreditMemosServiceTests
{
    private const string Item = "SOJA";
    private const string Customer = "C0001";
    private const decimal Quantity = 30_000m;
    private const int OriginSapDocEntry = 500;
    private static readonly DateTime Today = new(2026, 11, 16);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private readonly Guid _loadKey = Guid.NewGuid();
    private SalesInvoice _return = null!;

    private sealed class FakeFiscal(bool available, params SapFiscalDocumentInfo[] infos) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => available;

        public bool InvoicesAsked { get; private set; }

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default)
        {
            InvoicesAsked = true;
            return Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(
                new Dictionary<int, SapFiscalDocumentInfo>());
        }

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForCreditMemosAsync(
            IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(infos.ToDictionary(i => i.DocEntry));
    }

    private sealed class FailingFiscal(Exception failure) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => true;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            throw failure;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForCreditMemosAsync(
            IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default) =>
            throw failure;
    }

    private sealed class FailingSapService(AppDbContext context, SapErpDbContext sap, Exception failure)
        : SapLinkableCreditMemosService(context, sap, new FakeFiscal(false))
    {
        protected override Task<List<SapCreditMemoProjection>> ExecuteSapQueryAsync(
            IQueryable<SapCreditMemoProjection> query, CancellationToken ct) =>
            throw failure;
    }

    private sealed class FakeDbException() : DbException("SAP fora do ar");

    private SapLinkableCreditMemosService Service(ISapFiscalDocumentSource? fiscal = null) =>
        new(_db.Context, _sap, fiscal ?? new FakeFiscal(false));

    /// <summary>Devolução confirmada da carga, de uma origem vinculada à invoice 500 do SAP.</summary>
    private async Task SeedReturnAsync(Action<SalesInvoice>? tweak = null)
    {
        var origin = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceType = SalesInvoiceType.Normal,
            InvoiceStatus = InvoiceStatus.Confirmed, ShipmentLoadKey = _loadKey, SapDocEntry = OriginSapDocEntry,
        };
        _return = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceType = SalesInvoiceType.Return,
            InvoiceStatus = InvoiceStatus.Confirmed, ShipmentLoadKey = _loadKey, SalesInvoiceOriginKey = origin.Key,
            InvoiceDate = Today.AddDays(-5),
            Items = [new SalesInvoiceItem { ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = Quantity }],
        };
        tweak?.Invoke(_return);

        _db.Context.SalesInvoices.AddRange(origin, _return);
        await _db.SaveChangesAsync();
    }

    /// <summary>Credit Memo válido por padrão; cada teste estraga UMA regra.</summary>
    private async Task AddCreditMemoAsync(int docEntry, Action<SapCreditMemo>? tweak = null,
        Action<SapCreditMemoLine>? tweakLine = null, int extraLines = 0)
    {
        var creditMemo = new SapCreditMemo
        {
            DocEntry = docEntry, DocNum = docEntry + 2000, DocDate = Today.AddDays(-1), CardCode = Customer,
            CardName = "CLIENTE TESTE", BPLId = 3, Canceled = "N", Serial = 4512, SeriesStr = "1", Model = "39",
        };
        var line = new SapCreditMemoLine
        {
            DocEntry = docEntry, LineNum = 0, ItemCode = Item, Quantity = Quantity, Price = 2.5m,
            LineTotal = Quantity * 2.5m, CfopCode = "1201", Usage = 71,
        };
        tweak?.Invoke(creditMemo);
        tweakLine?.Invoke(line);

        _sap.SapCreditMemos.Add(creditMemo);
        _sap.SapCreditMemoLines.Add(line);
        for (var i = 1; i <= extraLines; i++)
            _sap.SapCreditMemoLines.Add(new SapCreditMemoLine
                { DocEntry = docEntry, LineNum = i, ItemCode = Item, Quantity = 1m });

        await _sap.SaveChangesAsync();
    }

    private Task<List<SapLinkableCreditMemoDto>> ListAsync(SapLinkableCreditMemosService? service = null) =>
        (service ?? Service()).ListAsync(_loadKey, _return.Key, Today.AddDays(-15), Today);

    [Fact]
    public async Task Valid_credit_memo_is_listed_with_formatted_number_series_and_fields()
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1);
        _sap.Usages.Add(new SiagroB1.Domain.Entities.SAP.Usage { Code = 71, Name = "DEVOLUCAO DE VENDA " });
        await _sap.SaveChangesAsync();

        var dto = Assert.Single(await ListAsync());

        Assert.Equal(1, dto.DocEntry);
        Assert.Equal(2001, dto.DocNum);
        Assert.Equal("000004512", dto.TaxDocumentNumber);
        Assert.Equal("001", dto.TaxDocumentSeries);
        Assert.Equal(Customer, dto.CardCode);
        Assert.Equal(Quantity, dto.Quantity);
        Assert.Equal(2.5m, dto.UnitPrice);
        Assert.Equal("1201", dto.Cfop);
        Assert.Equal(71, dto.UsageCode);
        Assert.Equal("DEVOLUCAO DE VENDA", dto.UsageName);
        Assert.True(dto.QuantityMatches);
        Assert.False(dto.MatchesOrigin);
        Assert.Null(dto.BaseInvoiceDocEntry);
        Assert.Null(dto.AccessKey);
    }

    public static IEnumerable<object[]> BrokenHeaders() =>
    [
        [(Action<SapCreditMemo>)(c => c.Canceled = "Y")],
        [(Action<SapCreditMemo>)(c => c.Canceled = "C")],
        [(Action<SapCreditMemo>)(c => c.Model = "0")],
        [(Action<SapCreditMemo>)(c => c.Serial = null)],
        [(Action<SapCreditMemo>)(c => c.BPLId = 4)],
        [(Action<SapCreditMemo>)(c => c.CardCode = "C9999")],
        [(Action<SapCreditMemo>)(c => c.DocDate = Today.AddDays(-16))],
        [(Action<SapCreditMemo>)(c => c.DocDate = Today.AddDays(1))],
    ];

    [Theory]
    [MemberData(nameof(BrokenHeaders))]
    public async Task Credit_memo_breaking_one_header_rule_is_not_listed(Action<SapCreditMemo> tweak)
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1, tweak);

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Credit_memo_of_another_item_or_with_two_lines_or_zero_quantity_is_not_listed()
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1, tweakLine: l => l.ItemCode = "MILHO");
        await AddCreditMemoAsync(2, extraLines: 1);
        await AddCreditMemoAsync(3, tweakLine: l => l.Quantity = 0m);

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Period_includes_the_whole_last_day()
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1, c => c.DocDate = Today.AddHours(18));

        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task Different_quantity_is_listed_but_flagged()
    {
        // Aparece para ajudar a achar o erro; quem barra é o vínculo.
        await SeedReturnAsync();
        await AddCreditMemoAsync(1, tweakLine: l => l.Quantity = Quantity - 500m);
        await AddCreditMemoAsync(2, tweakLine: l => l.Quantity = Quantity + 0.0005m);

        var list = await ListAsync();

        Assert.False(list.Single(d => d.DocEntry == 1).QuantityMatches);
        Assert.True(list.Single(d => d.DocEntry == 2).QuantityMatches);
    }

    [Fact]
    public async Task Credit_memo_that_references_the_origin_invoice_comes_first()
    {
        await SeedReturnAsync();
        // 1: mais recente, sem referência. 2: referencia outra invoice. 3: referencia a origem, mais antiga.
        await AddCreditMemoAsync(1);
        await AddCreditMemoAsync(2, c => c.DocDate = Today.AddDays(-2),
            l => { l.BaseType = 13; l.BaseEntry = 999; });
        await AddCreditMemoAsync(3, c => c.DocDate = Today.AddDays(-3),
            l => { l.BaseType = 13; l.BaseEntry = OriginSapDocEntry; });
        // BaseEntry igual ao da origem, mas de outro tipo de documento: não é a invoice.
        await AddCreditMemoAsync(4, c => c.DocDate = Today.AddDays(-4),
            l => { l.BaseType = 16; l.BaseEntry = OriginSapDocEntry; });

        var list = await ListAsync();

        Assert.Equal([3, 1, 2, 4], list.Select(d => d.DocEntry));
        Assert.True(list[0].MatchesOrigin);
        Assert.Equal(OriginSapDocEntry, list[0].BaseInvoiceDocEntry);
        Assert.False(list[2].MatchesOrigin);
        Assert.Equal(999, list[2].BaseInvoiceDocEntry);
        Assert.Null(list[3].BaseInvoiceDocEntry);
    }

    [Fact]
    public async Task Quantity_match_orders_before_date_when_nothing_references_the_origin()
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1, tweakLine: l => l.Quantity = 10m);
        await AddCreditMemoAsync(2, c => c.DocDate = Today.AddDays(-9));

        Assert.Equal([2, 1], (await ListAsync()).Select(d => d.DocEntry));
    }

    [Theory]
    [InlineData(InvoiceStatus.Confirmed, false)]
    [InlineData(InvoiceStatus.Pending, false)]
    [InlineData(InvoiceStatus.Cancelled, true)]
    [InlineData(InvoiceStatus.Returned, true)]
    public async Task Credit_memo_linked_to_another_active_document_is_not_listed(InvoiceStatus status, bool listed)
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1);
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceType = SalesInvoiceType.Return,
            InvoiceStatus = status, SapCreditMemoDocEntry = 1,
        });
        await _db.SaveChangesAsync();

        Assert.Equal(listed, (await ListAsync()).Count == 1);
    }

    [Fact]
    public async Task Sales_invoice_with_the_same_doc_entry_does_not_hide_the_credit_memo()
    {
        // DocEntry 1 de OINV vinculado a um documento de venda: outra sequência, outro documento.
        await SeedReturnAsync();
        await AddCreditMemoAsync(1);
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceStatus = InvoiceStatus.Confirmed,
            SapDocEntry = 1,
        });
        await _db.SaveChangesAsync();

        Assert.Single(await ListAsync());
    }

    [Fact]
    public async Task Find_still_sees_the_credit_memo_already_linked_to_this_same_return()
    {
        await SeedReturnAsync(r => r.SapCreditMemoDocEntry = 1);
        await AddCreditMemoAsync(1);

        Assert.NotNull(await Service().FindAsync(_loadKey, _return.Key, 1));
    }

    [Fact]
    public async Task With_the_fiscal_addon_only_authorized_and_not_cancelled_are_listed()
    {
        await SeedReturnAsync();
        for (var i = 1; i <= 4; i++) await AddCreditMemoAsync(i);
        var fiscal = new FakeFiscal(true,
            new SapFiscalDocumentInfo(1, "35261112345678000199550010000045121000012345", true, false, "135"),
            new SapFiscalDocumentInfo(2, null, false, false, null),
            new SapFiscalDocumentInfo(3, null, true, true, null));
        // 4: sem informação no add-on = não autorizada.

        var dto = Assert.Single(await ListAsync(Service(fiscal)));

        Assert.Equal(1, dto.DocEntry);
        Assert.Equal("35261112345678000199550010000045121000012345", dto.AccessKey);
        Assert.False(fiscal.InvoicesAsked); // nunca consulta o Credit Memo como invoice
    }

    [Fact]
    public async Task Without_the_fiscal_addon_nothing_is_filtered_and_there_is_no_key()
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1);

        var dto = Assert.Single(await ListAsync(Service(new FakeFiscal(false))));

        Assert.Null(dto.AccessKey);
    }

    [Fact]
    public async Task Find_applies_the_same_rules_without_the_period()
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1, c => c.DocDate = Today.AddDays(-200));
        await AddCreditMemoAsync(2, c => c.Canceled = "Y");

        Assert.Equal(1, (await Service().FindAsync(_loadKey, _return.Key, 1))!.DocEntry);
        Assert.Null(await Service().FindAsync(_loadKey, _return.Key, 2));
        Assert.Null(await Service().FindAsync(_loadKey, _return.Key, 77));
    }

    public static IEnumerable<object[]> NotReturnDocuments() =>
    [
        [(Action<SalesInvoice>)(r => r.InvoiceType = SalesInvoiceType.Normal)],
        [(Action<SalesInvoice>)(r => r.InvoiceStatus = InvoiceStatus.Pending)],
        [(Action<SalesInvoice>)(r => r.InvoiceStatus = InvoiceStatus.Cancelled)],
        [(Action<SalesInvoice>)(r => r.IsNfeReturn = true)],
        [(Action<SalesInvoice>)(r => r.ShipmentLoadKey = Guid.NewGuid())],
        [(Action<SalesInvoice>)(r => r.ShipmentLoadKey = null)],
    ];

    [Theory]
    [MemberData(nameof(NotReturnDocuments))]
    public async Task Document_that_is_not_a_confirmed_return_of_the_load_is_refused(Action<SalesInvoice> tweak)
    {
        await SeedReturnAsync(tweak);
        await AddCreditMemoAsync(1);

        var error = await Assert.ThrowsAsync<BusinessException>(() => ListAsync());

        Assert.Equal(SapLinkableCreditMemosService.NotReturnDocumentMessage, error.Message);
    }

    [Fact]
    public async Task Unknown_return_document_is_refused()
    {
        await SeedReturnAsync();

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Service().ListAsync(_loadKey, Guid.NewGuid(), Today.AddDays(-15), Today));

        Assert.Equal(SapLinkableCreditMemosService.NotReturnDocumentMessage, error.Message);
    }

    [Theory]
    [InlineData("03")]
    [InlineData("MATRIZ")]
    [InlineData(null)]
    public async Task Branch_that_is_not_the_sap_branch_id_lists_nothing(string? branchCode)
    {
        await SeedReturnAsync(r => r.BranchCode = branchCode);
        await AddCreditMemoAsync(1);

        Assert.Empty(await ListAsync());
    }

    public static IEnumerable<object[]> Failures() =>
    [
        [new FakeDbException()],
        [new TimeoutException("tempo esgotado")],
    ];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Sap_failure_becomes_a_business_error_in_list_and_find(Exception failure)
    {
        await SeedReturnAsync();
        var service = new FailingSapService(_db.Context, _sap, failure);

        var list = await Assert.ThrowsAsync<BusinessException>(() => ListAsync(service));
        var find = await Assert.ThrowsAsync<BusinessException>(() => service.FindAsync(_loadKey, _return.Key, 1));

        const string message = "Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes.";
        Assert.Equal(message, list.Message);
        Assert.Equal(message, find.Message);
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Fiscal_addon_failure_becomes_a_business_error(Exception failure)
    {
        await SeedReturnAsync();
        await AddCreditMemoAsync(1);

        var error = await Assert.ThrowsAsync<BusinessException>(() => ListAsync(Service(new FailingFiscal(failure))));

        Assert.Equal(
            "Não foi possível consultar a situação das notas fiscais no add-on fiscal. Tente novamente em instantes.",
            error.Message);
    }

    [Fact]
    public void Sap_queries_translate_to_sql_server()
    {
        // Provider SqlServer com connection string fictícia: ToQueryString não abre conexão, mas estoura em LINQ
        // que o InMemory aceitaria e o SQL Server não.
        using var sap = new SapErpDbContext(new DbContextOptionsBuilder<SapErpDbContext>()
            .UseSqlServer("Server=localhost;Database=dummy;User Id=x;Password=x;TrustServerCertificate=True").Options);

        var list = SapLinkableCreditMemosService.BuildSapQuery(
            sap, Item, 3, Customer, Today.AddDays(-15), Today.AddDays(1), null).ToQueryString();
        var find = SapLinkableCreditMemosService.BuildSapQuery(
            sap, Item, 3, Customer, null, null, 42).ToQueryString();

        foreach (var sql in new[] { list, find })
        {
            Assert.Contains("[ORIN]", sql);
            Assert.Contains("[RIN1]", sql);
            Assert.Contains("CANCELED", sql);
            Assert.Contains("BaseEntry", sql);
            Assert.DoesNotContain("OINV", sql);
            Assert.DoesNotContain("INV1]", sql.Replace("[RIN1]", string.Empty));
        }
    }
}
