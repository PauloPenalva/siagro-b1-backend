using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.Actions.SalesInvoices;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Spec 2026-10-18 S2/S4/S10: "Complementar ICMS" cria, a partir de uma venda Normal confirmada com NF-e autorizada, o
/// complemento Pendente com as linhas herdadas da original (valores zerados) e a base/ICMS informados por item.
/// </summary>
public class SalesInvoicesCreateIcmsComplementTests
{
    private const string V1 = "Só uma venda normal, confirmada, com NF-e autorizada pode ser complementada.";
    private const string V2 = "Informe a base e o valor do ICMS de ao menos um item.";
    private const string V8 = "Complemento de ICMS só existe na filial que emite NF-e pelo Siagro.";

    private static SalesInvoicesCreateIcmsComplementService Service(
        UnitOfWork db, string erp = "STANDALONE", FakeDocNumberSequenceService? numbers = null)
    {
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" },
            states: new() { [NfeTestSeed.CardCode] = "BA" });

        return new SalesInvoicesCreateIcmsComplementService(
            db, TaxTestServices.Gate(db, erp), TaxTestServices.Apply(db, partners, erp), numbers ?? new FakeDocNumberSequenceService(),
            NullLogger<SalesInvoicesCreateIcmsComplementService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    /// <summary>
    /// A venda autorizada do <see cref="NfeReturnTestSeed"/> com uma segunda linha (MILHO) e local de entrega, para provar
    /// a herança e o item que fica fora.
    /// </summary>
    private static async Task<(UnitOfWork Db, Guid SaleKey, Guid SojaKey, Guid MilhoKey)> SeedAsync()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        db.Context.Items.Add(new Item { ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", Ncm = "10059010", GoodsOrigin = 0 });
        // A natureza que emitiu a venda tem tributação: o motor (com valor 0) a exige para resolver o CFOP e o IBS/CBS.
        var usage = await db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos");
        usage.IcmsOutStateCst = "00";
        usage.PisCst = "01";
        usage.CofinsCst = "01";
        usage.IbsCbsCst = "000";
        usage.IbsCbsClassCode = "000001";
        var sale = await db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        var soja = sale.Items.Single();
        soja.Cfop = "6101";
        sale.DeliveryCardCode = "C-ENTREGA";
        sale.DeliveryCardName = "FAZENDA ENTREGA";
        sale.VolumeQuantity = 600;
        sale.VolumeSpecies = "SACOS";
        var milho = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", UnitOfMeasureCode = "SC",
            Quantity = 100m, UnitPrice = 50m, UsageCode = soja.UsageCode, UsageName = soja.UsageName, Cfop = "6102",
            Ncm = "10059010", GoodsOrigin = 0, CstIcms = "20", IcmsBase = 5000m, IcmsRate = 7m, IcmsValue = 350m,
            FreightValue = 10m, NfeItemNumber = 2, SalesInvoiceKey = sale.Key,
        };
        // Add explícito: com a chave já preenchida, o fixup pela navegação a marcaria como UPDATE.
        db.Context.SalesInvoicesItems.Add(milho);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return (db, sale.Key, soja.Key!.Value, milho.Key!.Value);
    }

    private static async Task<SalesInvoice> SavedAsync(UnitOfWork db, Guid? key)
    {
        db.Context.ChangeTracker.Clear();
        return await db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == key);
    }

    private static async Task<string> RejectsAsync(
        UnitOfWork db, Guid saleKey, IReadOnlyList<Guid> keys, IReadOnlyList<decimal> bases, IReadOnlyList<decimal> values,
        string erp = "STANDALONE")
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, erp).ExecuteAsync(saleKey, keys, bases, values, "tester"));
        return ex.Message;
    }

    private static async Task ChangeSaleAsync(UnitOfWork db, Guid saleKey, Action<SalesInvoice> change)
    {
        var sale = await db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == saleKey);
        change(sale);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task IcmsComplement_UsesOriginSeriesWithoutTypedCode()
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();
        var seriesKey = Guid.NewGuid();
        await ChangeSaleAsync(db, saleKey, s => s.DocNumberKey = seriesKey);
        var numbers = new FakeDocNumberSequenceService();

        var created = await Service(db, numbers: numbers).ExecuteAsync(saleKey, [sojaKey], [1_000m], [120m], "tester");

        var request = Assert.Single(numbers.Requests);
        Assert.Equal(TransactionCode.SalesInvoice, request.TransactionCode);
        Assert.Equal(seriesKey, request.DocNumberKey);
        Assert.Null(request.TypedCode);
        Assert.Equal(9, request.MaxLength);
        Assert.False(await request.ExistsAsync("NOPE"));
        var saved = await SavedAsync(db, created.Key);
        Assert.Equal(seriesKey, saved.DocNumberKey);
        Assert.StartsWith("ST-", saved.InvoiceNumber);
    }

    [Fact]
    public async Task Creates_complement_with_inherited_lines()
    {
        var (db, saleKey, sojaKey, milhoKey) = await SeedAsync();

        var created = await Service(db).ExecuteAsync(
            saleKey, [sojaKey, milhoKey], [1_000m, 500m], [120m, 35m], "tester");

        var saved = await SavedAsync(db, created.Key);
        Assert.Equal(SalesInvoiceType.IcmsComplement, saved.InvoiceType);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.Equal(TaxDocumentKind.Nfe, saved.TaxDocumentKind);
        Assert.Equal(NfeStatus.None, saved.NfeStatus);
        Assert.Equal(("01", NfeTestSeed.CardCode, "CLIENTE BA LTDA"), (saved.BranchCode, saved.CardCode, saved.CardName));
        Assert.Equal(("C-ENTREGA", "FAZENDA ENTREGA"), (saved.DeliveryCardCode, saved.DeliveryCardName));
        Assert.Equal(new DateTime(2026, 10, 2), saved.InvoiceDate!.Value.Date);
        Assert.False(string.IsNullOrEmpty(saved.InvoiceNumber));
        Assert.NotEqual("000002388", saved.InvoiceNumber);
        Assert.Equal("tester", saved.CreatedBy);
        // Sem pagamento, sem transporte, sem pesos/volumes, sem vínculo de devolução, carga ou liberação.
        Assert.Null(saved.PaymentConditionCode);
        Assert.True(string.IsNullOrEmpty(saved.TruckingCompanyCode));
        Assert.True(string.IsNullOrEmpty(saved.TruckCode));
        Assert.Equal(FreightTerms.None, saved.FreightTerms);
        Assert.Equal((0m, 0m), (saved.GrossWeight, saved.NetWeight));
        Assert.Null(saved.VolumeQuantity);
        Assert.Null(saved.VolumeSpecies);
        Assert.Null(saved.SalesInvoiceOriginKey);
        Assert.Null(saved.ShipmentLoadKey);
        Assert.Null(saved.ShipmentReleaseKey);
        Assert.False(saved.IsNfeReturn);
        Assert.Null(saved.TaxPayerComments);

        Assert.Equal(2, saved.Items.Count);
        var soja = saved.Items.Single(i => i.ComplementedItemKey == sojaKey);
        Assert.Equal(("SOJA", "SOJA EM GRAOS", "KG", "12019000"), (soja.ItemCode, soja.ItemName, soja.UnitOfMeasureCode, soja.Ncm));
        Assert.Equal(("6101", "00"), (soja.Cfop, soja.CstIcms));
        Assert.Equal((1_000m, 120m, 12.0000m), (soja.IcmsBase, soja.IcmsValue, soja.IcmsRate));
        Assert.Equal((0m, 0m, 0m), (soja.Quantity, soja.UnitPrice, soja.GrandTotal));
        Assert.Equal((0m, 0m, 0m, 0m), (soja.FreightValue, soja.InsuranceValue, soja.DiscountValue, soja.OtherExpensesValue));
        Assert.Null(soja.SalesContractKey);
        Assert.Null(soja.SalesShipmentReleaseKey);
        Assert.Null(soja.SalesInvoiceItemOriginKey);

        var milho = saved.Items.Single(i => i.ComplementedItemKey == milhoKey);
        Assert.Equal(("MILHO", "SC", "6102", "20"), (milho.ItemCode, milho.UnitOfMeasureCode, milho.Cfop, milho.CstIcms));
        Assert.Equal(soja.UsageCode, milho.UsageCode);
        Assert.Equal("Venda de grãos", milho.UsageName);
        Assert.Equal((500m, 35m, 0m), (milho.IcmsBase, milho.IcmsValue, milho.FreightValue));

        // A original não se move.
        var sale = await SavedAsync(db, saleKey);
        Assert.Equal((InvoiceStatus.Confirmed, NfeStatus.Authorized), (sale.InvoiceStatus, sale.NfeStatus));
    }

    [Fact]
    public async Task Item_with_zero_base_and_value_is_left_out()
    {
        var (db, saleKey, sojaKey, milhoKey) = await SeedAsync();

        var created = await Service(db).ExecuteAsync(saleKey, [sojaKey, milhoKey], [0m, 500m], [0m, 35m], "tester");

        var line = Assert.Single((await SavedAsync(db, created.Key)).Items);
        Assert.Equal(milhoKey, line.ComplementedItemKey);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("not-authorized")]
    [InlineData("no-key")]
    [InlineData("return")]
    [InlineData("icms-complement")]
    [InlineData("other-document")]
    [InlineData("cancelled")]
    public async Task Origin_not_normal_confirmed_authorized_is_refused(string state)
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();
        await ChangeSaleAsync(db, saleKey, sale =>
        {
            switch (state)
            {
                case "pending": sale.InvoiceStatus = InvoiceStatus.Pending; break;
                case "not-authorized": sale.NfeStatus = NfeStatus.Rejected; break;
                case "no-key": sale.ChaveNFe = null; break;
                case "return": sale.InvoiceType = SalesInvoiceType.Return; break;
                case "icms-complement": sale.InvoiceType = SalesInvoiceType.IcmsComplement; break;
                case "other-document": sale.TaxDocumentKind = TaxDocumentKind.Other; break;
                case "cancelled": sale.InvoiceStatus = InvoiceStatus.Cancelled; break;
            }
        });

        Assert.Equal(V1, await RejectsAsync(db, saleKey, [sojaKey], [1_000m], [120m]));
        Assert.Equal(1, await db.Context.SalesInvoices.CountAsync());
    }

    [Fact]
    public async Task Unknown_origin_is_not_found()
    {
        var (db, _, sojaKey, _) = await SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(db).ExecuteAsync(Guid.NewGuid(), [sojaKey], [1_000m], [120m], "tester"));
    }

    [Fact]
    public async Task All_items_zero_is_refused()
    {
        var (db, saleKey, sojaKey, milhoKey) = await SeedAsync();

        Assert.Equal(V2, await RejectsAsync(db, saleKey, [sojaKey, milhoKey], [0m, 0m], [0m, 0m]));
        Assert.Equal(V2, await RejectsAsync(db, saleKey, [], [], []));
        Assert.Equal(1, await db.Context.SalesInvoices.CountAsync());
    }

    [Theory]
    [InlineData(1_000, 0)]
    [InlineData(0, 120)]
    [InlineData(-10, 5)]
    public async Task Base_or_value_missing_on_a_line_is_refused(int icmsBase, int icmsValue)
    {
        var (db, saleKey, sojaKey, milhoKey) = await SeedAsync();

        Assert.Equal("Item MILHO: informe a base e o valor do ICMS do complemento.",
            await RejectsAsync(db, saleKey, [sojaKey, milhoKey], [1_000m, icmsBase], [120m, icmsValue]));
    }

    [Fact]
    public async Task Value_above_base_is_refused()
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();

        Assert.Equal("Item SOJA: o ICMS do complemento não pode ser maior que a base.",
            await RejectsAsync(db, saleKey, [sojaKey], [100m], [100.01m]));
    }

    [Fact]
    public async Task Item_from_another_document_is_refused()
    {
        var (db, saleKey, _, _) = await SeedAsync();

        Assert.Equal("Item informado não pertence ao documento de saída.",
            await RejectsAsync(db, saleKey, [Guid.NewGuid()], [100m], [10m]));
    }

    [Fact]
    public async Task Parallel_lists_with_different_sizes_are_refused()
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();

        Assert.Equal("A lista de itens e as de base e ICMS têm tamanhos diferentes.",
            await RejectsAsync(db, saleKey, [sojaKey], [100m], []));
    }

    [Theory]
    [InlineData("STANDALONE", false)]
    [InlineData("SAPB1", true)]
    public async Task Branch_without_tax_rule_is_refused(string erp, bool issuesNfe)
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();
        (await db.Context.Branchs.SingleAsync()).IssuesNfe = issuesNfe;
        await db.SaveChangesAsync();

        Assert.Equal(V8, await RejectsAsync(db, saleKey, [sojaKey], [1_000m], [120m], erp));
    }

    [Fact]
    public async Task Origin_from_shipment_load_with_contract_is_accepted()
    {
        // Review Focus 2: ao contrário da devolução, a venda de carga/romaneio/contrato pode ser complementada — e o
        // complemento não leva contrato, liberação nem carga.
        var (db, saleKey, sojaKey, _) = await SeedAsync();
        await ChangeSaleAsync(db, saleKey, sale =>
        {
            sale.ShipmentLoadKey = Guid.NewGuid();
            sale.ShipmentReleaseKey = Guid.NewGuid();
            var item = sale.Items.Single(i => i.Key == sojaKey);
            item.SalesContractKey = Guid.NewGuid();
            item.SalesShipmentReleaseKey = Guid.NewGuid();
        });

        var created = await Service(db).ExecuteAsync(saleKey, [sojaKey], [1_000m], [120m], "tester");

        var saved = await SavedAsync(db, created.Key);
        Assert.Null(saved.ShipmentLoadKey);
        Assert.Null(saved.ShipmentReleaseKey);
        var line = Assert.Single(saved.Items);
        Assert.Null(line.SalesContractKey);
        Assert.Null(line.SalesShipmentReleaseKey);
    }

    [Fact]
    public async Task Two_complements_for_same_origin_are_allowed()
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();

        var first = await Service(db).ExecuteAsync(saleKey, [sojaKey], [1_000m], [120m], "tester");
        var second = await Service(db).ExecuteAsync(saleKey, [sojaKey], [200m], [24m], "tester");

        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(2, await db.Context.SalesInvoices.CountAsync(i => i.InvoiceType == SalesInvoiceType.IcmsComplement));
    }

    [Fact]
    public void Edm_exposes_the_action_with_parallel_double_lists()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var action = builder.GetEdmModel().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "SalesInvoicesCreateIcmsComplement");

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Collection(Edm.Guid)", action.Parameters.Single(p => p.Name == "OriginItemKeys").Type.FullName());
        Assert.Equal("Collection(Edm.Double)", action.Parameters.Single(p => p.Name == "IcmsBases").Type.FullName());
        Assert.Equal("Collection(Edm.Double)", action.Parameters.Single(p => p.Name == "IcmsValues").Type.FullName());
        Assert.Equal("Edm.Guid", action.ReturnType.FullName());
    }

    [Fact]
    public async Task Controller_creates_and_returns_the_new_document_key()
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();
        var controller = new SalesInvoicesCreateIcmsComplementController(Service(db));

        var result = await controller.PostAsync(new ODataActionParameters
        {
            ["Key"] = saleKey,
            ["OriginItemKeys"] = new List<Guid> { sojaKey },
            ["IcmsBases"] = new List<double> { 1_000.5 },
            ["IcmsValues"] = new List<double> { 120.06 },
        });

        var key = Assert.IsType<Guid>(Assert.IsType<OkObjectResult>(result).Value);
        var line = Assert.Single((await SavedAsync(db, key)).Items);
        Assert.Equal((1_000.5m, 120.06m), (line.IcmsBase, line.IcmsValue));
    }

    [Fact]
    public async Task Controller_turns_business_refusal_into_bad_request()
    {
        var (db, saleKey, sojaKey, _) = await SeedAsync();
        var controller = new SalesInvoicesCreateIcmsComplementController(Service(db));

        var result = await controller.PostAsync(new ODataActionParameters
        {
            ["Key"] = saleKey,
            ["OriginItemKeys"] = new List<Guid> { sojaKey },
            ["IcmsBases"] = new List<double> { 0 },
            ["IcmsValues"] = new List<double> { 0 },
        });

        // ODataController.BadRequest(string) devolve BadRequestODataResult, não BadRequestObjectResult.
        Assert.Equal(V2, Assert.IsType<BadRequestODataResult>(result).Error.Message);
    }
}
