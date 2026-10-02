using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Documento + cadastro carregado → entrada do XML, normalizada.</summary>
public class NfeIssueInputAssemblerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(-3));

    private static async Task<NfeIssueInput> BuildAsync(NfeScenario scenario, Action<SalesInvoice>? change = null)
    {
        var invoice = await scenario.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "48151623";
        change?.Invoke(invoice);
        await scenario.Db.SaveChangesAsync();

        var options = new NfeOptions(NfeTestSeed.Config());
        var context = await new NfeReadinessValidator(scenario.Db, options).ValidateAsync(invoice);

        return NfeIssueInputAssembler.Build(invoice, context, IssuedAt, options.TechnicalResponsible);
    }

    [Fact]
    public async Task Masked_tax_ids_are_sent_only_with_alphanumerics()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.Branchs.SingleAsync()).TaxId = "12.345.678/0001-95";
        (await scenario.Db.Context.BusinessPartners.SingleAsync(p => p.CardCode == NfeTestSeed.CardCode)).TaxId = "11.222.333/0001-81";
        await scenario.Db.SaveChangesAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal("12345678000195", input.Issuer.TaxId);
        Assert.Equal("11222333000181", input.Recipient.TaxId);
    }

    [Fact]
    public async Task Identification_comes_from_the_document_and_the_settings()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal(NfeEnvironment.Homologation, input.Environment);
        Assert.Equal(1, input.Series);
        Assert.Equal(1L, input.Number);
        Assert.Equal("48151623", input.RandomCode);
        Assert.Equal("000000001", input.BillingNumber);
        Assert.Equal(IssuedAt, input.IssuedAt);
        Assert.Equal("3521705", input.Issuer.Address.MunicipalityCode);
        Assert.Equal("2927408", input.Recipient.Address.MunicipalityCode);
        Assert.Equal("09123456000100", input.TechnicalResponsible!.Cnpj);
    }

    [Fact]
    public async Task Operation_nature_comes_from_the_first_line_usage()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        Assert.Equal("VENDA DE PRODUCAO DO ESTABELECIMENTO", (await BuildAsync(scenario)).OperationNature);
    }

    [Fact]
    public async Task Operation_nature_falls_back_to_the_usage_name()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.Usages.SingleAsync()).InvoiceOperationText = null;
        await scenario.Db.SaveChangesAsync();

        Assert.Equal("Venda de grãos", (await BuildAsync(scenario)).OperationNature);
    }

    [Fact]
    public async Task Additional_info_joins_usage_text_and_taxpayer_comments()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal("Produto agrícola in natura. | Pedido do cliente 77", input.AdditionalInfo);
    }

    [Fact]
    public async Task Line_carries_the_recorded_taxes()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var item = Assert.Single((await BuildAsync(scenario)).Items);

        Assert.Equal(1, item.Number);
        Assert.Equal("6102", item.Cfop);
        Assert.Equal("00", item.IcmsCode);
        Assert.Equal(4200m, item.IcmsValue);
        Assert.Equal(60000m, item.Total);
        Assert.Equal(455.75m, item.CbsValue);
    }

    [Fact]
    public async Task Payment_follows_the_condition_and_the_document_total()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var payment = (await BuildAsync(scenario)).Payment;

        Assert.Equal("15", payment.PaymentMeans);
        Assert.Equal([30000m, 30000m], payment.Installments.Select(i => i.Amount));
        Assert.Equal(new DateOnly(2026, 11, 1), payment.Installments[0].DueDate);
    }

    [Fact]
    public async Task Vehicle_plate_is_normalized_and_state_comes_from_the_truck()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal(new NfeVehicle("ABC1D23", "SP"), input.Vehicle);
        Assert.Equal("TRANSPORTADORA TESTE LTDA", input.Carrier!.Name);
        Assert.Equal("RUA B, 10", input.Carrier.FullAddress);
    }

    [Fact]
    public async Task Delivery_only_when_another_partner_receives()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        Assert.Null((await BuildAsync(scenario)).Delivery);
        Assert.Null((await BuildAsync(scenario, i => i.DeliveryCardCode = NfeTestSeed.CardCode)).Delivery);
    }
}
