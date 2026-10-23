using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Services.EfdContributions;
using static SiagroB1.Application.Tests.EfdContributions.EfdContributionsFileServiceTests;

namespace SiagroB1.Application.Tests.EfdContributions;

public class EfdContributionsValidatorTests
{
    [Fact]
    public async Task Validate_MissingRevenueNatureBlocks()
    {
        var db = await Seeded();
        // Venda CST 09 com a natureza sem "Natureza da receita"
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "09", 0m, 0m, usage: SaleUsage));
        await Save(db);

        var issues = await Issues(db);

        Assert.Contains(issues, i => i.Blocking && i.Message == $"Natureza {SaleUsage} sem Natureza da receita (EFD Contribuições) para CST 09.");
        Assert.Single(issues, i => i.Message.Contains("Natureza da receita"));
    }

    [Fact]
    public async Task Validate_MissingCreditBaseBlocks()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative);
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 500m, "50", 1.65m, 7.6m, usage: SaleUsage));
        await Save(db);

        var issues = await Issues(db);

        Assert.Contains(issues, i => i.Blocking && i.Message == $"Natureza {SaleUsage} sem Base de cálculo do crédito (EFD Contribuições).");
    }

    [Fact]
    public async Task Validate_CreditBaseNotRequiredInCumulative()
    {
        var db = await Seeded();
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 500m, "50", 1.65m, 7.6m, usage: SaleUsage));
        await Save(db);

        Assert.DoesNotContain(await Issues(db), i => i.Blocking);
    }

    [Fact]
    public async Task Validate_Cst05RequiresRevenueNature()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "05", 0m, 0m, usage: SaleUsage));
        await Save(db);

        Assert.Contains(await Issues(db), i => i.Blocking && i.Message == $"Natureza {SaleUsage} sem Natureza da receita (EFD Contribuições) para CST 05.");
    }

    [Fact]
    public async Task Validate_OutputCst03Blocks()
    {
        var db = await Seeded();
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "03", 0m, 0m));
        await Save(db);

        Assert.Contains(await Issues(db), i => i.Blocking
            && i.Message == "Linha 123/10001 com CST 03 de PIS/COFINS (alíquota por unidade) não suportado na EFD Contribuições.");
    }

    [Fact]
    public async Task Validate_EntryPresumedCreditCstBlocks()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative);
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 500m, "60", 1.65m, 7.6m));
        await Save(db);

        Assert.Contains(await Issues(db), i => i.Blocking
            && i.Message.StartsWith("Linha ") && i.Message.EndsWith(" com CST 60 (crédito presumido) não suportado na EFD Contribuições."));
    }

    [Fact]
    public async Task Validate_CreditCstInCumulativeBranchIsLeftOut()
    {
        // Filial cumulativa não apura crédito: a entrada nem entra no arquivo, então não cobra nada.
        var db = await Seeded();
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 500m, "50", 1.65m, 7.6m, usage: SaleUsage));
        await Save(db);

        Assert.DoesNotContain(await Issues(db), i => i.Message.StartsWith("Linha "));
    }

    [Fact]
    public async Task Validate_LineWithoutCstBlocks()
    {
        var db = await Seeded();
        var sale = Sale("V1", "01", 1000m, "01", 0.65m, 3m);
        sale.Items.Single().CstCofins = null;
        db.Context.SalesInvoices.Add(sale);
        await Save(db);

        Assert.Contains(await Issues(db), i => i.Blocking && i.Message == "Linha 123/10001 sem CST de PIS ou COFINS.");
    }

    [Fact]
    public async Task Validate_NoHeadOfficeBlocks()
    {
        var db = await Seeded(headOffice: false, withBranch02: true, settings: false);

        var issues = await Issues(db, "02");

        Assert.Contains(issues, i => i.Blocking && i.Message == "Filiais do CNPJ 12345678 sem matriz (/0001) cadastrada.");
    }

    [Fact]
    public async Task Validate_MissingAccountantFieldsBlock()
    {
        var db = await Seeded();
        var s = db.Context.SpedFiscalSettings.Single();
        s.AccountantCrc = "";
        await Save(db);

        Assert.Contains(await Issues(db), i => i.Blocking && i.Message == "Parâmetros do SPED Fiscal: informe CRC do contabilista.");
    }

    [Fact]
    public async Task Validate_RectifyingWithoutReceiptBlocks()
    {
        var db = await Seeded();
        var request = Request();
        request.Purpose = 1;

        var issues = EfdContributionsValidator.Validate(await Loader(db).LoadAsync(request), request);

        Assert.Contains(issues, i => i.Blocking && i.Message.Contains("recibo"));
    }

    [Fact]
    public async Task Validate_CleanMonthHasNoBlocking()
    {
        var db = await Seeded(regime01: PisCofinsRegime.NonCumulative);
        db.Context.SalesInvoices.Add(Sale("V1", "01", 1000m, "01", 1.65m, 7.6m));
        db.Context.PurchaseInvoices.Add(ThirdPartyPurchase("P1", "01", 500m, "50", 1.65m, 7.6m));
        await Save(db);

        Assert.DoesNotContain(await Issues(db), i => i.Blocking);
    }

    private static async Task<List<SiagroB1.Reports.Dtos.EfdContributionsIssueDto>> Issues(SiagroB1.Infra.IUnitOfWork db, string branch = "01")
    {
        var request = Request(branch);
        return EfdContributionsValidator.Validate(await Loader(db).LoadAsync(request), request);
    }
}
