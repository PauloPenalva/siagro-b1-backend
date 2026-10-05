using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// O documento de saída nasce com a condição de pagamento padrão do cliente quando chega sem ela
/// (inclusive pelo faturamento de romaneio). Em SAPB1 o parceiro não tem o campo e nada muda.
/// </summary>
public class SalesInvoicesPaymentConditionDefaultTests
{
    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "SP" });
        await db.SaveChangesAsync();

        await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            RequiresQuantity = true, IsDefault = true,
        });

        return db;
    }

    private static SalesInvoicesCreateService Create(UnitOfWork db, int? customerDefault)
    {
        var partners = new FakeBusinessPartnerService(
            names: new() { ["C1"] = "CLIENTE" },
            states: new() { ["C1"] = "SP" },
            paymentConditions: customerDefault is { } code ? new() { ["C1"] = code } : null);
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesCreateService(
            db, partners,
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static SalesInvoice Invoice(int? paymentCondition = null) => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", InvoiceDate = new DateTime(2026, 10, 2),
        PaymentConditionCode = paymentCondition,
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
                SalesContractKey = Guid.NewGuid(),
            },
        ],
    };

    [Fact]
    public async Task Empty_condition_receives_the_customer_default()
    {
        var db = await SeedAsync();
        var invoice = Invoice();

        await Create(db, customerDefault: 7).ExecuteAsync(invoice, "tester");

        Assert.Equal(7, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Informed_condition_is_kept()
    {
        var db = await SeedAsync();
        var invoice = Invoice(paymentCondition: 5);

        await Create(db, customerDefault: 7).ExecuteAsync(invoice, "tester");

        Assert.Equal(5, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Customer_without_default_keeps_it_empty()
    {
        var db = await SeedAsync();
        var invoice = Invoice();

        await Create(db, customerDefault: null).ExecuteAsync(invoice, "tester");

        Assert.Null(invoice.PaymentConditionCode);
    }
}
