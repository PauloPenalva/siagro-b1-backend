using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record PurchaseNfeScenario(UnitOfWork Db, string DatabaseName, Guid InvoiceKey, Guid SaleKey, int ReturnUsage);

/// <summary>
/// Compra de trigo de produtor de Itaberá/SP (não contribuinte, CPF) com NF-e própria de entrada: CFOP 1102,
/// ICMS 51 diferido com cBenef, PIS/COFINS 74 — o formato das 23 compras reais da CEAGUI (spec §1).
/// </summary>
public static class PurchaseNfeTestSeed
{
    public const string Supplier = "F-SP";
    public const string ProducerKey = "35261011222333000181550010000004561123456780";

    public static async Task<PurchaseNfeScenario> SeedAsync(bool twoItems = false)
    {
        var sale = await NfeTestSeed.SeedAsync();
        var context = sale.Db.Context;
        var condition = await context.PaymentConditions.FirstAsync();

        context.Items.AddRange(
            new Item { ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", Ncm = "10019900", GoodsOrigin = 0 },
            new Item { ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", Ncm = "10059010", GoodsOrigin = 0 });

        var returnUsage = new Usage
        {
            Name = "DEVOLUCAO DE COMPRA", Direction = UsageDirection.Outgoing, InvoiceOperationText = "DEVOLUCAO DE COMPRA",
            CfopOutgoingInState = "5202", CfopOutgoingOutState = "6202", IcmsInStateCst = "51", IcmsInStateRate = 18m,
            IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00",
            PisCst = "49", CofinsCst = "49",
        };
        context.Usages.Add(returnUsage);
        await sale.Db.SaveChangesAsync();

        var purchaseUsage = new Usage
        {
            Name = "COMPRA DE MERCADORIA", Direction = UsageDirection.Incoming, InvoiceOperationText = "COMPRA DE MERCADORIA",
            DefaultAdditionalInfo = "Produtor optante pelo recolhimento pela folha de pagamento.",
            CfopIncomingInState = "1102", CfopIncomingOutState = "2102", IcmsInStateCst = "51", IcmsInStateRate = 18m,
            IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00",
            PisCst = "74", CofinsCst = "74", ReturnUsageCode = returnUsage.Code,
        };
        context.Usages.Add(purchaseUsage);

        context.BusinessPartners.Add(new BusinessPartner
        {
            CardCode = Supplier, CardName = "PRODUTOR RURAL TESTE", CardType = "S", TaxId = "52998224725",
            StateRegistrationIndicator = StateRegistrationIndicator.NonTaxpayer, PaymentConditionCode = condition.Code,
            Addresses =
            [
                new Address
                {
                    CardCode = Supplier, AddressName = "FATURAMENTO", AdresType = "B", Street = "ESTRADA MUNICIPAL",
                    StreetNumber = "S/N", Block = "ZONA RURAL", ZipCode = "18440000", City = "Itaberá", State = "SP",
                    Country = "BR", MunicipalityCode = "3521705",
                },
            ],
        });
        await sale.Db.SaveChangesAsync();

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, CardName = "PRODUTOR RURAL TESTE",
            IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Pending,
            IssueDate = new DateTime(2026, 10, 2), PostingDate = new DateTime(2026, 10, 2), GrossWeight = 1000m, NetWeight = 1000m,
            FreightTerms = FreightTerms.None, PaymentConditionCode = condition.Code, ReferencedAccessKey = ProducerKey,
            TaxPayerComments = "Contribuição Social denominada Funrural 1,5% = R$ 22,50",
        };
        invoice.AddItem(Line("TRIGO", "TRIGO EM GRAOS", "10019900", 1000m, purchaseUsage));
        if (twoItems)
            invoice.AddItem(Line("MILHO", "MILHO EM GRAOS", "10059010", 500m, purchaseUsage));
        if (twoItems)
        {
            invoice.GrossWeight = 1500m;
            invoice.NetWeight = 1500m;
        }

        context.PurchaseInvoices.Add(invoice);
        await sale.Db.SaveChangesAsync();

        return new PurchaseNfeScenario(sale.Db, sale.DatabaseName, invoice.Key, sale.InvoiceKey, returnUsage.Code);
    }

    /// <summary>Linha já calculada: base = total, ICMS 51 18% diferido 100% (vICMSOp = vICMSDif, vICMS 0).</summary>
    private static PurchaseInvoiceItem Line(string code, string name, string ncm, decimal quantity, Usage usage)
    {
        var total = decimal.Round(quantity * 1.5m, 2);
        var operation = decimal.Round(total * 0.18m, 2, MidpointRounding.AwayFromZero);

        return new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = code, ItemName = name, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 1.5m,
            UsageCode = usage.Code, UsageName = usage.Name, Cfop = "1102", Ncm = ncm, GoodsOrigin = 0,
            CstIcms = "51", IcmsBase = total, IcmsRate = 18m, IcmsOperationValue = operation, IcmsDeferral = 100m,
            IcmsDeferredValue = operation, IcmsValue = 0m, IcmsBenefitCode = "SP053521", CstPis = "74", CstCofins = "74",
        };
    }
}
