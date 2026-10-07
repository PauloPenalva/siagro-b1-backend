using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record NfeReturnScenario(NfeScenario Sale, int ReturnUsageCode, Guid SaleItemKey);

/// <summary>
/// A venda do <see cref="NfeTestSeed"/> já AUTORIZADA (série 1 nº 1) e confirmada, com uma natureza
/// de devolução de entrada (1202/2202) que reproduz a tributação dela, vinculada à natureza da venda.
/// </summary>
public static class NfeReturnTestSeed
{
    public const string SaleAccessKey = "35261012345678000195550010000000011481516230";

    public static async Task<NfeReturnScenario> SeedAsync()
    {
        var sale = await NfeTestSeed.SeedAsync();
        var context = sale.Db.Context;

        context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", Ncm = "12019000", GoodsOrigin = 0 });
        context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m });
        await sale.Db.SaveChangesAsync();

        var returnUsage = await new UsageService(sale.Db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Entrada devolução", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1202", CfopIncomingOutState = "2202", InvoiceOperationText = "DEVOLUCAO DE VENDA",
            IcmsInStateCst = "00", IcmsInStateRate = 18m, IcmsOutStateCst = "00",
            PisCst = "72", CofinsCst = "72", IbsCbsCst = "000", IbsCbsClassCode = "000001",
            RequiresQuantity = true,
        });

        var saleUsage = await context.Usages.SingleAsync(u => u.Name == "Venda de grãos");
        saleUsage.ReturnUsageCode = returnUsage.Code;

        var invoice = await context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == sale.InvoiceKey);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        invoice.ChaveNFe = SaleAccessKey;
        invoice.NfeRandomCode = "48151623";
        invoice.Items.Single().NfeItemNumber = 1;
        await sale.Db.SaveChangesAsync();

        return new NfeReturnScenario(sale, returnUsage.Code, invoice.Items.Single().Key!.Value);
    }

    public static SalesInvoicesNfeReturnCreateService CreateService(UnitOfWork db, string erp = "STANDALONE")
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" },
            states: new() { [NfeTestSeed.CardCode] = "BA" });

        var create = new SalesInvoicesCreateService(
            db, partners, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.Apply(db, partners, erp),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);

        return new SalesInvoicesNfeReturnCreateService(
            db, TaxTestServices.Gate(db, erp), create, NullLogger<SalesInvoicesNfeReturnCreateService>.Instance,
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    public static Task<SalesInvoice> CreateReturnAsync(NfeReturnScenario scenario, decimal quantity, string reason = "Carga recusada") =>
        CreateService(scenario.Sale.Db).ExecuteAsync(
            new SalesInvoiceNfeReturnRequest(
                scenario.Sale.InvoiceKey, [new SalesInvoiceNfeReturnItem(scenario.SaleItemKey, quantity)], reason),
            "tester");
}
