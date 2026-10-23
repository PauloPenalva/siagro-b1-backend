using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Reports.Services.EfdContributions;
using SiagroB1.Reports.Services.Sped;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.EfdContributions;

public class EfdContributionsDataLoaderTests
{
    [Fact]
    public async Task Load_GroupsBranchesBySameCnpjRoot()
    {
        var db = await Seeded(("02", "12.345.678/0002-76"), ("01", "12.345.678/0001-95"));
        db.Context.Usages.Add(new Usage { Code = 7, Name = "VENDA" });
        var s1 = OwnSale("V1", "01");
        s1.Items.Single().UsageCode = 7;
        db.Context.SalesInvoices.AddRange(s1, OwnSale("V2", "02"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request("02"));

        Assert.Equal(new[] { "01", "02" }, data.Establishments.Select(e => e.Branch.Code));
        Assert.Equal(new[] { "V1" }, data.Establishments[0].Lines.Select(l => l.InternalNumber));
        Assert.Equal(new[] { "V2" }, data.Establishments[1].Lines.Select(l => l.InternalNumber));
        Assert.Equal(7, data.Establishments[0].Lines[0].UsageCode);
        Assert.Equal(new[] { 7 }, data.Usages.Keys);
        Assert.Equal("C001", Assert.Single(data.Establishments[0].MasterData.MissingParticipantCodes));
    }

    [Fact]
    public async Task Load_IgnoresBranchWithOtherCnpjRoot()
    {
        var db = await Seeded(("01", "12.345.678/0001-95"), ("09", "98.765.432/0001-10"));
        db.Context.SalesInvoices.Add(OwnSale("X", "09"));
        await Save(db);

        var data = await Loader(db).LoadAsync(Request("01"));

        var e = Assert.Single(data.Establishments);
        Assert.Equal("01", e.Branch.Code);
        Assert.Empty(e.Lines);
    }

    [Fact]
    public async Task Load_DeclarantIsHeadOffice0001()
    {
        var db = await Seeded(("02", "12.345.678/0002-76"), ("01", "12.345.678/0001-95"));
        db.Context.SpedFiscalSettings.Add(new SpedFiscalSettings { BranchCode = "01" });
        await Save(db);

        var data = await Loader(db).LoadAsync(Request("02"));

        Assert.Equal("01", data.Declarant?.Code);
        Assert.Equal("01", data.Settings?.BranchCode);
    }

    [Fact]
    public async Task Load_NoHeadOfficeLeavesDeclarantNull()
    {
        var db = await Seeded(("02", "12.345.678/0002-76"));

        var data = await Loader(db).LoadAsync(Request("02"));

        Assert.Null(data.Declarant);
        Assert.Null(data.Settings);
        Assert.Single(data.Establishments);
    }

    [Fact]
    public async Task Load_BranchWithoutTaxIdThrows()
    {
        var db = await Seeded(("01", null));

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Loader(db).LoadAsync(Request("01")));
        Assert.Equal("Filial sem CNPJ.", ex.Message);
    }

    private static SalesInvoice OwnSale(string number, string branchCode)
    {
        var line = SaleItem();
        line.Cfop = "5101";
        var sale = Sale(number, branchCode: branchCode, items: line);
        sale.TaxDocumentKind = TaxDocumentKind.Nfe;
        sale.NfeStatus = NfeStatus.Authorized;
        return sale;
    }

    private static async Task<IUnitOfWork> Seeded(params (string Code, string? TaxId)[] branches)
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var (code, taxId) in branches)
            db.Context.Branchs.Add(new Branch { Code = code, BranchName = "FILIAL " + code, TaxId = taxId });
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA", Ncm = "12019000" });
        db.Context.UnitsOfMeasure.Add(new UnitOfMeasure { Code = "TN", Description = "TONELADA" });
        await Save(db);
        return db;
    }

    private static EfdContributionsDataLoader Loader(IUnitOfWork db) =>
        new(db, new FiscalConferenceService(db), new SpedMasterDataLoader(db));

    private static EfdContributionsRequest Request(string branch) => new() { BranchCode = branch, Year = 2026, Month = 7 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
