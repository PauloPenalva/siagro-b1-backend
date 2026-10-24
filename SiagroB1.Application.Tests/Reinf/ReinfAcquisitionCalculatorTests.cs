using SiagroB1.Application.Services.Reinf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using Xunit;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfAcquisitionCalculatorTests
{
    private const string Root = "12345678";
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    public ReinfAcquisitionCalculatorTests()
    {
        var c = _db.Context;
        c.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", TaxId = "12.345.678/0001-90" });
        c.Branchs.Add(new Branch { Code = "99", BranchName = "Outra", TaxId = "99999999000100" });
        c.BusinessPartners.Add(Partner("P1", "11122233344"));
        c.Usages.Add(new Usage { Code = 1, Name = "Compra", RuralContributionMode = RuralContributionMode.Full });
        c.Usages.Add(new Usage { Code = 2, Name = "Senar", RuralContributionMode = RuralContributionMode.SenarOnly,
            RuralJudicialProcessNumber = "1234567", RuralJudicialSuspensionCode = "01" });
        c.Usages.Add(new Usage { Code = 3, Name = "Senar sem processo", RuralContributionMode = RuralContributionMode.SenarOnly });
        c.RuralContributionRates.Add(new RuralContributionRate { StartDate = new DateOnly(2020, 1, 1), Scheme = RuralContributionScheme.Standard, InssRate = 1.20m, RatRate = 0.10m, SenarRate = 0.20m });
        c.RuralContributionRates.Add(new RuralContributionRate { StartDate = new DateOnly(2020, 1, 1), Scheme = RuralContributionScheme.PayrollOption, InssRate = 0m, RatRate = 0m, SenarRate = 0.25m });
        c.SaveChanges();
    }

    private static BusinessPartner Partner(string code, string taxId, string? cpf = null, bool payroll = false) => new()
    {
        CardCode = code, CardName = "Produtor " + code, TaxId = taxId, RuralProducerCpf = cpf, RuralPayrollOption = payroll,
        IsRuralProducer = true, RuralProducerType = RuralProducerType.Individual,
    };

    private PurchaseInvoiceItem Invoice(string card, DateTime date, decimal gross, int usage = 1, string branch = "01",
        NfeStatus nfe = NfeStatus.None, bool senarOnly = false)
    {
        var inv = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = card, BranchCode = branch, IssueDate = date, InvoiceStatus = InvoiceStatus.Confirmed, NfeStatus = nfe };
        var item = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = inv.Key, UsageCode = usage, FunruralBase = gross,
            FunruralInssValue = senarOnly ? 0 : Math.Round(gross * 0.012m, 2), FunruralRatValue = senarOnly ? 0 : Math.Round(gross * 0.001m, 2),
            FunruralSenarValue = Math.Round(gross * 0.002m, 2),
        };
        _db.Context.PurchaseInvoices.Add(inv);
        _db.Context.PurchaseInvoicesItems.Add(item);
        _db.Context.SaveChanges();
        return item;
    }

    private void Return(PurchaseInvoiceItem origin, DateTime date, decimal gross)
    {
        var inv = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "P1", BranchCode = "01", IssueDate = date, InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = PurchaseInvoiceType.Return, IsNfeReturn = true };
        _db.Context.PurchaseInvoices.Add(inv);
        _db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = inv.Key, PurchaseInvoiceItemOriginKey = origin.Key, FunruralBase = gross,
            FunruralInssValue = Math.Round(gross * 0.012m, 2), FunruralRatValue = Math.Round(gross * 0.001m, 2), FunruralSenarValue = Math.Round(gross * 0.002m, 2),
        });
        _db.Context.SaveChanges();
    }

    private Task<(IReadOnlyList<ReinfAcquisitionRow> Rows, IReadOnlyList<ReinfIssue> Issues)> Run() =>
        new ReinfAcquisitionCalculator(_db.Context).CalculateAsync(Root, "2026-09", CancellationToken.None);

    [Fact]
    public async Task Sums_lines_of_same_producer_and_branch()
    {
        Invoice("P1", new DateTime(2026, 9, 5), 1000m);
        Invoice("P1", new DateTime(2026, 9, 20), 500m);
        Invoice("P1", new DateTime(2026, 8, 31), 700m); // outro mês
        Invoice("P1", new DateTime(2026, 9, 10), 300m, nfe: NfeStatus.Cancelled);
        Invoice("P1", new DateTime(2026, 9, 10), 300m, branch: "99"); // outra raiz

        var (rows, issues) = await Run();

        var row = Assert.Single(rows);
        Assert.Equal("12345678000190", row.EstablishmentTaxId);
        Assert.Equal(1500.00m, row.Gross);
        Assert.Equal(18.00m, row.Inss);
        Assert.Equal(2, row.ProducerInscriptionType);
        Assert.Equal("11122233344", row.ProducerTaxId);
        Assert.Equal(1, Assert.Single(row.Details).AcquisitionIndicator);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Return_from_other_month_is_deducted_and_total_return_keeps_zero_row()
    {
        var origin = Invoice("P1", new DateTime(2026, 9, 5), 1500m);
        Return(origin, new DateTime(2026, 10, 2), 200m);
        var (rows, _) = await Run();
        Assert.Equal(1300m, Assert.Single(rows).Gross);

        Return(origin, new DateTime(2026, 10, 3), 1300m);
        (rows, _) = await Run();
        Assert.Equal(0m, Assert.Single(rows).Gross);
    }

    [Fact]
    public async Task Payroll_option_flags_row()
    {
        _db.Context.BusinessPartners.Add(Partner("P2", "22233344455", payroll: true));
        _db.Context.SaveChanges();
        Invoice("P2", new DateTime(2026, 9, 5), 1000m, senarOnly: true);

        var row = Assert.Single((await Run()).Rows);
        Assert.True(row.PayrollOption);
        Assert.Equal(0m, row.Inss);
        Assert.Equal(2.00m, row.Senar);
        Assert.Equal(1, Assert.Single(row.Details).AcquisitionIndicator);
    }

    [Fact]
    public async Task Senar_only_usage_yields_indicator_4_with_process_and_not_withheld()
    {
        Invoice("P1", new DateTime(2026, 9, 5), 1000m, usage: 2, senarOnly: true);
        Invoice("P1", new DateTime(2026, 9, 6), 500m, usage: 1);

        var (rows, issues) = await Run();
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Details.Count);
        var d4 = row.Details.Single(d => d.AcquisitionIndicator == 4);
        Assert.Equal("1234567", d4.JudicialProcess);
        Assert.Equal("01", d4.SuspensionCode);
        Assert.Equal(12.00m, d4.InssNotWithheld);
        Assert.Equal(1.00m, d4.RatNotWithheld);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Senar_only_without_process_raises_issue()
    {
        Invoice("P1", new DateTime(2026, 9, 5), 1000m, usage: 3, senarOnly: true);
        var (_, issues) = await Run();
        Assert.Contains(issues, i => i.Message == "Natureza 3 em 'Somente SENAR' sem processo judicial.");
    }

    [Fact]
    public async Task Cnpj_producer_with_cpf_reports_cpf_and_invalid_taxid_raises_issue()
    {
        _db.Context.BusinessPartners.Add(Partner("P3", "11222333000144", cpf: "99988877766"));
        _db.Context.BusinessPartners.Add(Partner("P4", "123"));
        _db.Context.SaveChanges();
        Invoice("P3", new DateTime(2026, 9, 5), 1000m);
        Invoice("P4", new DateTime(2026, 9, 5), 1000m);

        var (rows, issues) = await Run();
        var p3 = rows.Single(r => r.CardCode == "P3");
        Assert.Equal(2, p3.ProducerInscriptionType);
        Assert.Equal("99988877766", p3.ProducerTaxId);
        Assert.Contains(issues, i => i.Message == "Produtor P4 sem CPF/CNPJ válido.");
        Assert.Equal(new[] { "P3", "P4" }, rows.Select(r => r.CardCode));
    }

    [Fact]
    public async Task Hash_changes_when_gross_changes()
    {
        Invoice("P1", new DateTime(2026, 9, 5), 1000m);
        var before = Assert.Single((await Run()).Rows).ContentHash;
        Assert.Equal(before, Assert.Single((await Run()).Rows).ContentHash);
        Assert.Matches("^[0-9a-f]{64}$", before);

        Invoice("P1", new DateTime(2026, 9, 6), 10m);
        Assert.NotEqual(before, Assert.Single((await Run()).Rows).ContentHash);
    }
}
