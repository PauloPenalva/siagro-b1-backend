using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.WithholdingTaxSettings;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Spec 2026-10-17 §2: retenções de serviço na natureza (alíquotas padrão) e no documento (fotografia editável).
///
/// Migration <c>SeedWithholdingTaxCreditors</c> (SQL puro, sem harness relacional nos testes, ruling R1): insere os
/// parceiros TRIB-FEDERAL, TRIB-ESTADUAL e TRIB-MUNICIPAL (CardType 'S') e as 6 configurações (ISS → municipal dia 10;
/// IRRF, INSS, PIS, COFINS, CSLL → federal dia 20), cada uma só <c>WHERE NOT EXISTS</c>. A conferência real vai para a Task 8.
/// </summary>
public class PurchaseInvoiceWithholdingsTests
{
    private static UsageModel Usage(string name, decimal? iss = null, decimal? irrf = null) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1933", CfopIncomingOutState = "2933",
        CreatesFinancialDocument = true, WithholdingIssRate = iss, WithholdingIrrfRate = irrf,
    };

    private static async Task<(UsageService Service, int Code)> SeedUsageAsync(UsageModel model)
    {
        var service = new UsageService(TestDb.CreateUnitOfWork(), NullLogger<UsageService>.Instance);
        return (service, (await service.CreateAsync(model)).Code);
    }

    private static PurchaseInvoice Invoice(int usageCode, decimal price = 1000m)
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F001" };
        invoice.AddItem(new PurchaseInvoiceItem
            { Key = Guid.NewGuid(), ItemCode = "SERV", Quantity = 1m, UnitPrice = price, UsageCode = usageCode });
        return invoice;
    }

    [Fact]
    public async Task Computes_withholdings_from_usage_rates()
    {
        var (usages, code) = await SeedUsageAsync(Usage("Serviço tomado", iss: 5m, irrf: 1.5m));
        var invoice = Invoice(code);

        await PurchaseInvoiceWithholdings.ApplyAsync(invoice, usages, otherPurchase: true);

        Assert.Equal((1000m, 50.00m, 15.00m), (invoice.WithholdingBase, invoice.WithheldIss, invoice.WithheldIrrf));
        Assert.Equal(0m, invoice.WithheldInss + invoice.WithheldPis + invoice.WithheldCofins + invoice.WithheldCsll);
        Assert.Equal(65m, invoice.TotalWithheld);
        Assert.Equal(935m, invoice.NetPayable);
    }

    [Fact]
    public async Task Usage_rates_round_trip_through_the_usage_service()
    {
        var (usages, code) = await SeedUsageAsync(Usage("Serviço tomado", iss: 5m, irrf: 1.5m));

        var read = await usages.GetByIdAsync(code);

        Assert.Equal((5m, 1.5m, (decimal?)null), (read!.WithholdingIssRate, read.WithholdingIrrfRate, read.WithholdingCsllRate));
    }

    [Fact]
    public async Task Manual_values_are_preserved_on_save()
    {
        var (usages, code) = await SeedUsageAsync(Usage("Serviço tomado", iss: 5m, irrf: 1.5m));
        var invoice = Invoice(code);
        invoice.WithholdingsManual = true;
        invoice.WithholdingBase = 1000m;
        invoice.WithheldIss = 30m;

        await PurchaseInvoiceWithholdings.ApplyAsync(invoice, usages, otherPurchase: true);

        Assert.Equal((30m, 0m), (invoice.WithheldIss, invoice.WithheldIrrf));
    }

    [Fact]
    public async Task Withholdings_cleared_when_not_other_purchase()
    {
        var (usages, code) = await SeedUsageAsync(Usage("Grão", iss: 5m));
        var invoice = Invoice(code);
        invoice.WithholdingBase = 1000m;
        invoice.WithheldIss = 50m;
        invoice.WithholdingsManual = true;

        await PurchaseInvoiceWithholdings.ApplyAsync(invoice, usages, otherPurchase: false);

        Assert.Equal(0m, invoice.WithholdingBase + invoice.TotalWithheld);
    }

    [Fact]
    public async Task Withheld_above_total_is_refused()
    {
        var (usages, code) = await SeedUsageAsync(Usage("Serviço tomado"));
        var invoice = Invoice(code);
        invoice.WithholdingsManual = true;
        invoice.WithheldIss = 1000.01m;

        await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoiceWithholdings.ApplyAsync(invoice, usages, otherPurchase: true));
    }

    [Fact]
    public async Task Negative_withheld_is_refused()
    {
        var (usages, code) = await SeedUsageAsync(Usage("Serviço tomado"));
        var invoice = Invoice(code);
        invoice.WithholdingsManual = true;
        invoice.WithheldIrrf = -1m;

        await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoiceWithholdings.ApplyAsync(invoice, usages, otherPurchase: true));
    }

    [Fact]
    public async Task Lines_with_different_rates_are_refused()
    {
        var usages = new UsageService(TestDb.CreateUnitOfWork(), NullLogger<UsageService>.Instance);
        var a = (await usages.CreateAsync(Usage("Serviço A", iss: 5m))).Code;
        var b = (await usages.CreateAsync(Usage("Serviço B", iss: 2m))).Code;
        var invoice = Invoice(a);
        invoice.AddItem(new PurchaseInvoiceItem
            { Key = Guid.NewGuid(), ItemCode = "SERV2", Quantity = 1m, UnitPrice = 500m, UsageCode = b });

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoiceWithholdings.ApplyAsync(invoice, usages, otherPurchase: true));

        Assert.Equal("As linhas têm naturezas com alíquotas de retenção diferentes.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task Withholding_setting_due_day_out_of_range_is_refused(int dueDay)
    {
        var service = new WithholdingTaxSettingsService(TestDb.CreateUnitOfWork(), null!);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(
            new WithholdingTaxSetting { TaxType = (int)WithholdingTaxType.Iss, CardCode = "TRIB-MUNICIPAL", DueDay = dueDay }));

        Assert.Equal("O dia de vencimento deve estar entre 1 e 31.", ex.Message);
    }
}
