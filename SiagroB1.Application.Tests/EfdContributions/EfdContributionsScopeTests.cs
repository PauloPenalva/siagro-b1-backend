using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.EfdContributions;
using SiagroB1.Reports.Services.Sped;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.EfdContributions;

/// <summary>Só entram na EFD Contribuições as notas que são receita ou que geram crédito.</summary>
public class EfdContributionsScopeTests
{
    [Theory]
    [InlineData("5102")]
    [InlineData("6102")]
    [InlineData("7101")]
    [InlineData("5922")]
    [InlineData("5932")]
    [InlineData("5933")]
    public void Output_RevenueCfopIsDeclared(string cfop) =>
        Assert.True(EfdContributionsScope.IsDeclared(Line("S", cfop, "09"), nonCumulative: false));

    [Theory]
    [InlineData("5905")] // remessa para depósito/armazém
    [InlineData("6905")]
    [InlineData("5906")] // retorno de depósito
    [InlineData("5949")] // outra saída não especificada
    [InlineData("5152")] // transferência
    [InlineData("5202")] // devolução de compra
    [InlineData("5551")] // venda do ativo imobilizado
    public void Output_NonRevenueCfopIsLeftOut(string cfop) =>
        Assert.False(EfdContributionsScope.IsDeclared(Line("S", cfop, "08"), nonCumulative: true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entry_SalesReturnIsDeclaredInBothRegimes(bool nonCumulative) =>
        Assert.True(EfdContributionsScope.IsDeclared(Line("E", "1202", "98"), nonCumulative));

    [Fact]
    public void Entry_CreditCstIsDeclaredOnlyWhenNonCumulative()
    {
        Assert.True(EfdContributionsScope.IsDeclared(Line("E", "1102", "50"), nonCumulative: true));
        Assert.False(EfdContributionsScope.IsDeclared(Line("E", "1102", "50"), nonCumulative: false));
    }

    [Theory]
    [InlineData("74")]
    [InlineData("98")]
    [InlineData("")]
    public void Entry_WithoutCreditIsLeftOut(string cst) =>
        Assert.False(EfdContributionsScope.IsDeclared(Line("E", "1102", cst), nonCumulative: true));

    [Fact]
    public void LineWithoutCfopIsDeclared_SoTheValidatorCanBlockIt() =>
        Assert.True(EfdContributionsScope.IsDeclared(Line("S", "", "09"), nonCumulative: false));

    [Fact]
    public async Task Load_KeepsOnlyDeclaredLinesAndNoVoidDocuments()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", TaxId = "12.345.678/0001-95",
            PisCofinsRegime = PisCofinsRegime.Cumulative,
        });
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA", Ncm = "12019000" });
        db.Context.SalesInvoices.Add(OwnSale("VENDA", "5102"));
        db.Context.SalesInvoices.Add(OwnSale("REMESSA", "5905"));
        var cancelled = OwnSale("CANCELADA", "5102");
        cancelled.NfeStatus = NfeStatus.Cancelled;
        db.Context.SalesInvoices.Add(cancelled);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var data = await new EfdContributionsDataLoader(db, new FiscalConferenceService(db), new SpedMasterDataLoader(db))
            .LoadAsync(new EfdContributionsRequest { BranchCode = "01", Year = 2026, Month = 7 });

        var e = Assert.Single(data.Establishments);
        Assert.Equal(new[] { "VENDA" }, e.Lines.Select(l => l.InternalNumber).Distinct());
        Assert.Empty(e.VoidDocuments);
    }

    private static FiscalDocumentLineDto Line(string direction, string cfop, string cst) => new()
    {
        Direction = direction, Cfop = cfop, CstPis = cst, CstCofins = cst,
    };

    private static SalesInvoice OwnSale(string number, string cfop)
    {
        var line = SaleItem();
        line.Cfop = cfop;
        line.CstPis = line.CstCofins = "09";
        var sale = Sale(number, items: line);
        sale.TaxDocumentKind = TaxDocumentKind.Nfe;
        sale.NfeStatus = NfeStatus.Authorized;
        return sale;
    }
}
