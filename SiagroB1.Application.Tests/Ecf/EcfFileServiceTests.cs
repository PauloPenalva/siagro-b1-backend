using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;
using SiagroB1.Reports.Services.Ecf;
using EcdTests = SiagroB1.Application.Tests.Ecd.EcdFileServiceTests;

namespace SiagroB1.Application.Tests.Ecf;

public class EcfFileServiceTests
{
    private static readonly CultureInfo Pt = new("pt-BR");

    // ---- cenário: o da ECD (receita 500 em março no CC01, despesa 200 em maio, apuração em 31/12) + ECF ----

    internal static Task<IUnitOfWork> SeedAsync(EcfAssessmentForm form = EcfAssessmentForm.Annual, Action<IUnitOfWork>? customize = null) =>
        EcdTests.SeedAsync(customize: db =>
        {
            var matriz = db.Context.Branchs.Local.Single(b => b.Code == "01");
            (matriz.Street, matriz.StreetNumber, matriz.District, matriz.ZipCode) = ("RUA DA SAFRA", "100", "CENTRO", "01001-000");
            db.Context.EcfSettings.Add(new EcfSettings
                { BranchCode = "01", AssessmentForm = form, MainCnae = "0115600", LegalEntityQualification = "01" });
            db.Context.EcfPartners.AddRange(
                new EcfPartner { BranchCode = "01", Name = "ANA SÓCIA", TaxId = "52998224725", Qualification = "05",
                    TotalCapitalPercent = 60m, VotingCapitalPercent = 60m },
                new EcfPartner { BranchCode = "01", Name = "HOLDING SA", TaxId = "11222333000181", Qualification = "05",
                    TotalCapitalPercent = 40m, VotingCapitalPercent = 40m });
            customize?.Invoke(db);
        });

    private static EcfAdjustment Adjustment(int period, EcfAdjustmentTax tax, EcfAdjustmentKind kind, string code, decimal amount,
        string? partB = null, string? account = null) => new()
    {
        BranchCode = "01", Year = 2026, Period = period, Tax = tax, Kind = kind, TableCode = code, Description = "Ajuste " + code,
        Amount = amount, PartBAccountCode = partB, PartBAccountName = partB == null ? null : "Conta " + partB, LedgerAccountCode = account,
    };

    internal static EcfRequest Request() => new() { BranchCode = "02", Year = 2026 };

    internal static EcfFileService Service(IUnitOfWork db) => new(new EcfDataLoader(db, new EcdDataLoader(db)));

    private static List<string[]> Parse(byte[] content)
    {
        var text = Encoding.Latin1.GetString(content);
        Assert.EndsWith("\r\n", text);
        return text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(l => l[1..^1].Split('|')).ToList();
    }

    private static async Task<List<string[]>> GenerateAsync(IUnitOfWork db, EcfRequest? request = null) =>
        Parse((await Service(db).GenerateAsync(request ?? Request())).Content);

    /// <summary>Monta direto da carga, sem o validador (cenários sem a apuração de resultado).</summary>
    private static async Task<(List<string[]> Lines, EcfData Data)> BuildAsync(IUnitOfWork db)
    {
        var data = await new EcfDataLoader(db, new EcdDataLoader(db)).LoadAsync(Request());
        return (Parse(EcfFileService.Build(data, Request())), data);
    }

    private static List<string[]> Of(List<string[]> lines, string reg) => lines.Where(l => l[0] == reg).ToList();

    private static decimal Dec(string s) => decimal.Parse(s, Pt);

    /// <summary>Registros de um período (depois do X030 com PER_APUR até o próximo X030).</summary>
    private static List<string[]> PeriodRows(List<string[]> lines, string periodReg, string period, string reg)
    {
        var start = lines.FindIndex(l => l[0] == periodReg && l[3] == period);
        Assert.True(start >= 0, $"{periodReg} {period} ausente");
        return lines.Skip(start + 1).TakeWhile(l => l[0] != periodReg && l[0][0] == periodReg[0]).Where(l => l[0] == reg).ToList();
    }

    private static decimal Value(List<string[]> rows, EcfTableLine line) => Dec(rows.Single(r => r[1] == line.Code)[3]);

    // ---- geração ----

    [Fact]
    public async Task Generate_HeaderBlocksAndBlock9Counts()
    {
        var db = await SeedAsync();
        var (content, fileName) = await Service(db).GenerateAsync(Request());
        var lines = Parse(content);

        Assert.Equal("ECF_12345678000195_2026.txt", fileName);
        Assert.Equal(["0000", "LECF", "0012", "12345678000195", "AGRO TESTE LTDA", "0", "0", "", "", "01012026", "31122026", "N", "", "0", ""],
            lines[0]);
        Assert.Equal(["0010", "", "N", "1", "A", "01", "EEEE", "BBBBBBBBBBBB", "", "", "", "D", ""], Of(lines, "0010").Single());
        Assert.Equal(33, Of(lines, "0020").Single().Length);
        Assert.Equal(["0030", "2062", "0115600", "RUA DA SAFRA", "100", "", "CENTRO", "SP", "3550308", "01001000", "", "joao@agro.com"],
            Of(lines, "0030").Single());
        Assert.Equal(["0930", "JOÃO DIRETOR", "52998224725", "205", "", "joao@agro.com", "11999990000"], Of(lines, "0930")[0]);
        Assert.Equal(["0930", "MARIA CONTADORA", "11144477735", "900", "SP123456O8", "maria@contab.com", "1133334444"], Of(lines, "0930")[1]);

        foreach (var r in Of(lines, "9900"))
            Assert.Equal(lines.Count(l => l[0] == r[1]), int.Parse(r[2]));
        var blocks = new[] { '0', 'C', 'E', 'J', 'K', 'L', 'M', 'N', 'P', 'Q', 'T', 'U', 'V', 'W', 'X', 'Y' };
        foreach (var block in blocks)
            Assert.Equal(lines.Count(l => l[0][0] == block), int.Parse(Of(lines, $"{block}990").Single()[1]));
        // Ordem dos blocos do leiaute
        Assert.Equal(blocks.Append('9'), lines.Select(l => l[0][0]).Distinct());
        Assert.Equal(lines.Count.ToString(), lines[^1][1]);
        Assert.Equal(["C001", "1"], Of(lines, "C001").Single());
    }

    [Fact]
    public async Task Generate_BlocksJAndKMatchTheEcd()
    {
        var db = await SeedAsync();
        var ecf = await GenerateAsync(db);
        var (ecdContent, _) = await EcdTests.Service(db).GenerateAsync(EcdTests.Request());
        var ecd = Parse(ecdContent);

        Assert.Equal(Of(ecd, "I050").Select(l => l[1..]), Of(ecf, "J050").Select(l => l[1..]));
        Assert.Equal(Of(ecd, "I051").Select(l => l[1..]), Of(ecf, "J051").Select(l => l[1..]));
        Assert.Equal(Of(ecd, "I100").Select(l => l[1..]), Of(ecf, "J100").Select(l => l[1..]));
        // K355 (A00) = I355 da apuração de 31/12
        Assert.Equal(Of(ecd, "I355").Select(l => l[1..]), PeriodRows(ecf, "K030", "A00", "K355").Select(l => l[1..]));

        // K155 do ano = I155 de janeiro (inicial) + soma dos meses + dezembro (final), só patrimoniais
        var i155 = Of(ecd, "I155").Where(l => l[1][0] is '1' or '2').ToList();
        var k155 = PeriodRows(ecf, "K030", "A00", "K155");
        Assert.Equal(i155.Select(l => (l[1], l[2])).Distinct().Order(), k155.Select(l => (l[1], l[2])).Order());
        foreach (var k in k155)
        {
            var months = i155.Where(l => l[1] == k[1] && l[2] == k[2]).ToList();
            Assert.Equal(months[0][3..5], k[3..5]);
            Assert.Equal(months.Sum(l => Dec(l[5])), Dec(k[5]));
            Assert.Equal(months.Sum(l => Dec(l[6])), Dec(k[6]));
            Assert.Equal(months[^1][7..9], k[7..9]);
        }

        Assert.Equal(["K155", "1.1.01", "", "1000,00", "D", "500,00", "200,00", "1300,00", "D"], k155.Single(l => l[1] == "1.1.01"));
        Assert.Equal(["K156", "REF", "1000,00", "D", "500,00", "200,00", "1300,00", "D"], ecf[ecf.FindLastIndex(l => l[0] == "K155" && l[1] == "1.1.01") + 1]);
        Assert.Equal(["K030", "01012026", "31122026", "A00"], Of(ecf, "K030")[^1]);
    }

    [Fact]
    public async Task Generate_AnnualBalancetesA01ToA11AreCumulative()
    {
        var lines = await GenerateAsync(await SeedAsync());

        var codes = Enumerable.Range(1, 11).Select(m => $"A{m:00}").Append("A00").ToList();
        foreach (var reg in new[] { "K030", "L030" }) Assert.Equal(codes, Of(lines, reg).Select(l => l[3]));
        Assert.Equal(["K030", "01012026", "31052026", "A05"], Of(lines, "K030")[4]);
        Assert.Equal(Enumerable.Range(1, 12).Select(m => $"A{m:00}").Append("A00"), Of(lines, "N030").Select(l => l[3]));

        // Fevereiro: nada no resultado; março: só a receita; maio em diante: acumulado receita + despesa
        Assert.Empty(PeriodRows(lines, "K030", "A02", "K355"));
        Assert.Equal([["K355", "3.1.01", "CC01", "500,00", "C"]], PeriodRows(lines, "K030", "A03", "K355"));
        Assert.Equal([["K355", "3.1.01", "CC01", "500,00", "C"], ["K355", "4.1.01", "CC01", "200,00", "D"]],
            PeriodRows(lines, "K030", "A05", "K355"));
        Assert.Equal(["K155", "1.1.01", "", "1000,00", "D", "500,00", "200,00", "1300,00", "D"],
            PeriodRows(lines, "K030", "A05", "K155").Single(l => l[1] == "1.1.01"));
        Assert.Equal(["L300", "REF", "REF", "A", "1", "04", "", "300,00", "C"], PeriodRows(lines, "L030", "A05", "L300").Single());
        Assert.Equal(2, PeriodRows(lines, "K030", "A05", "K155").Count);

        // N500/N650 nos balancetes A01..A11 (acumulado, sem ajustes); dezembro só estimativa
        Assert.Equal(500m, Value(PeriodRows(lines, "N030", "A04", "N500"), EcfTableCodes.N500TaxableIncome));
        Assert.Equal(300m, Value(PeriodRows(lines, "N030", "A11", "N650"), EcfTableCodes.N650Base));
        Assert.Empty(PeriodRows(lines, "N030", "A12", "N500"));
        Assert.Equal(12, PeriodRows(lines, "N030", "A00", "N500").Count + PeriodRows(lines, "N030", "A00", "N650").Count);
    }

    [Fact]
    public async Task Generate_BlockLSumsMappedAccountsByReferential()
    {
        var lines = await GenerateAsync(await SeedAsync());

        // Todas as contas mapeiam para "REF": patrimoniais 1000 D − 1000 C no início; no fim 1300 D − 1300 C
        Assert.Equal(["L100", "REF", "REF", "A", "1", "01", "", "0,00", "D", "700,00", "700,00", "0,00", "D"],
            PeriodRows(lines, "L030", "A00", "L100").Single());
        // Resultado antes da apuração: receita 500 C + despesa 200 D = 300 C
        Assert.Equal(["L300", "REF", "REF", "A", "1", "04", "", "300,00", "C"], PeriodRows(lines, "L030", "A00", "L300").Single());
    }

    [Fact]
    public async Task Generate_AnnualPartAAndTaxMatchTheCalculator()
    {
        var db = await SeedAsync(customize: d => d.Context.EcfAdjustments.AddRange(
            Adjustment(0, EcfAdjustmentTax.Both, EcfAdjustmentKind.Addition, "93", 100m, partB: "B01", account: "4.1.01"),
            Adjustment(0, EcfAdjustmentTax.Irpj, EcfAdjustmentKind.Exclusion, "173", 50m),
            Adjustment(0, EcfAdjustmentTax.Csll, EcfAdjustmentKind.LossOffset, "P1", 30m)));
        var lines = await GenerateAsync(db);

        var irpj = IncomeTaxCalculator.ComputeIrpj(300m, 100m, 50m, 0m, 12);
        var csll = IncomeTaxCalculator.ComputeCsll(300m, 100m, 0m, 30m);

        var m300 = PeriodRows(lines, "M030", "A00", "M300");
        Assert.Equal(["M300", "1", "Lucro Líquido antes do IRPJ", "L", "4", "300,00", ""], m300[0]);
        Assert.Equal(["M300", "93", "Ajuste 93", "A", "3", "100,00", "Ajuste 93"], m300[1]);
        Assert.Equal(["M300", "173", "Ajuste 173", "E", "4", "50,00", "Ajuste 173"], m300[2]);
        Assert.Equal(3, m300.Count);
        var m350 = PeriodRows(lines, "M030", "A00", "M350");
        Assert.Equal(["93", "P1"], m350.Skip(1).Select(r => r[1]));
        Assert.Equal("P", m350[2][3]);
        Assert.Equal(["M305", "B01", "100,00", "D"], Of(lines, "M305").Single());
        Assert.Equal(["M310", "4.1.01", "", "100,00", "D"], Of(lines, "M310").Single());
        Assert.Equal(2, Of(lines, "M010").Count);
        Assert.Equal(["M500", "B01", "I", "0,00", "D", "100,00", "D", "0,00", "D", "100,00", "D"], Of(lines, "M500")[0]);

        var n500 = PeriodRows(lines, "N030", "A00", "N500");
        Assert.Equal(irpj.TaxableBase, Value(n500, EcfTableCodes.N500TaxableIncome));
        Assert.Equal(350m, Value(n500, EcfTableCodes.N500TaxableIncome));
        var n630 = PeriodRows(lines, "N030", "A00", "N630");
        Assert.Equal(irpj.TaxableBase, Value(n630, EcfTableCodes.N630Base));
        Assert.Equal(irpj.Tax, Value(n630, EcfTableCodes.N630Tax));
        Assert.Equal(irpj.Surtax, Value(n630, EcfTableCodes.N630Surtax));
        Assert.Equal(irpj.Total, Value(n630, EcfTableCodes.N630Due));
        var n670 = PeriodRows(lines, "N030", "A00", "N670");
        Assert.Equal(csll.TaxableBase, Value(n670, EcfTableCodes.N670Base));
        Assert.Equal(370m, csll.TaxableBase);
        Assert.Equal(csll.Tax, Value(n670, EcfTableCodes.N670Tax));
        Assert.Equal(30m, Value(PeriodRows(lines, "N030", "A00", "N650"), EcfTableCodes.N650LossOffset));

        // Estimativas: lucro só a partir de março (500), maio cai para 300 → nada a pagar depois de março
        Assert.Equal(75m, Value(PeriodRows(lines, "N030", "A03", "N620"), EcfTableCodes.N620Payable));
        Assert.Equal(0m, Value(PeriodRows(lines, "N030", "A05", "N620"), EcfTableCodes.N620Payable));
        Assert.Equal(75m, Value(n630, EcfTableCodes.N630Estimates));
        Assert.Equal(Math.Max(0m, irpj.Total - 75m), Value(n630, EcfTableCodes.N630Payable));
        Assert.Equal(45m, Value(n670, EcfTableCodes.N670Estimates));
        Assert.Equal(12, Of(lines, "N620").Count / 6);
    }

    [Fact]
    public async Task MonthlyEstimates_CumulativeBalanceMinusPreviouslyDueNeverNegative()
    {
        // Janeiro 300.000 de lucro, fevereiro −100.000, março +150.000 (acumulados 300k, 200k, 350k)
        var db = await SeedAsync(customize: d => d.Context.JournalEntries.AddRange(
            new JournalEntry { PostingDate = new DateTime(2026, 1, 15), BranchCode = "01", Batch = "1", DocumentNumber = "1", CreatedBy = "t",
                Origin = JournalEntryOrigin.Manual, Lines = [new JournalEntryLine { LineNumber = 1, EntryType = JournalEntryLineType.Double,
                    DebitAccountCode = "1.1.01", CreditAccountCode = "3.1.01", Amount = 300_000m, History = "V" }] },
            new JournalEntry { PostingDate = new DateTime(2026, 2, 15), BranchCode = "01", Batch = "1", DocumentNumber = "2", CreatedBy = "t",
                Origin = JournalEntryOrigin.Manual, Lines = [new JournalEntryLine { LineNumber = 1, EntryType = JournalEntryLineType.Double,
                    DebitAccountCode = "4.1.01", CreditAccountCode = "1.1.01", Amount = 100_000m, History = "D" }] },
            new JournalEntry { PostingDate = new DateTime(2026, 3, 15), BranchCode = "01", Batch = "1", DocumentNumber = "3", CreatedBy = "t",
                Origin = JournalEntryOrigin.Manual, Lines = [new JournalEntryLine { LineNumber = 1, EntryType = JournalEntryLineType.Double,
                    DebitAccountCode = "1.1.01", CreditAccountCode = "3.1.01", Amount = 149_500m, History = "V" }] }));
        var data = await new EcfDataLoader(db, new EcdDataLoader(db)).LoadAsync(Request());

        var e = EcfAssessment.MonthlyEstimates(data, 2026);

        Assert.Equal([300_000m, 200_000m, 350_000m], e.Take(3).Select(x => x.CumulativeProfit));
        // jan: 45.000 + 10% × 280.000 = 73.000; fev: devido acumulado 46.000 < 73.000 → 0
        Assert.Equal(73_000m, e[0].Irpj);
        Assert.Equal(0m, e[1].Irpj);
        // mar: 52.500 + 10% × 290.000 = 81.500 − 73.000 = 8.500
        Assert.Equal(8_500m, e[2].Irpj);
        Assert.Equal(73_000m, e[2].IrpjPreviouslyDue);
        // CSLL: 27.000; 18.000 → 0; 31.500 − 27.000 = 4.500
        Assert.Equal([27_000m, 0m, 4_500m], e.Take(3).Select(x => x.Csll));
    }

    [Fact]
    public async Task Generate_QuarterlyHasFourPeriodsWithOwnProfitAndSurtaxThreshold()
    {
        var db = await SeedAsync(EcfAssessmentForm.Quarterly, d => d.Context.EcfAdjustments.Add(
            Adjustment(1, EcfAdjustmentTax.Both, EcfAdjustmentKind.Addition, "93", 100_000m)));
        var lines = await GenerateAsync(db);

        Assert.Equal(["0010", "", "N", "1", "T", "01", "RRRR", "", "", "", "", "T", ""], Of(lines, "0010").Single());
        foreach (var reg in new[] { "K030", "L030", "M030", "N030" })
            Assert.Equal(["T01", "T02", "T03", "T04"], Of(lines, reg).Select(l => l[3]));
        Assert.Empty(Of(lines, "N620"));
        Assert.Equal(["K030", "01042026", "30062026", "T02"], Of(lines, "K030")[1]);

        // T01: 500 + 100.000 = 100.500; adicional sobre 60.000 do trimestre
        var t1 = PeriodRows(lines, "N030", "T01", "N630");
        var irpj = IncomeTaxCalculator.ComputeIrpj(500m, 100_000m, 0m, 0m, 3);
        Assert.Equal(irpj.Surtax, Value(t1, EcfTableCodes.N630Surtax));
        Assert.Equal(4_050m, irpj.Surtax);
        Assert.Equal(irpj.Total, Value(t1, EcfTableCodes.N630Payable));
        Assert.Equal(9_045m, Value(PeriodRows(lines, "N030", "T01", "N670"), EcfTableCodes.N670Tax));

        // T02: prejuízo de 200 → IRPJ, adicional e CSLL zero
        Assert.Equal(-200m, Value(PeriodRows(lines, "N030", "T02", "N500"), EcfTableCodes.N500BookProfit));
        Assert.All(PeriodRows(lines, "N030", "T02", "N630"), r => Assert.Equal(0m, Dec(r[3])));
        Assert.All(PeriodRows(lines, "N030", "T02", "N670"), r => Assert.Equal(0m, Dec(r[3])));
        Assert.Equal(["K355", "4.1.01", "CC01", "200,00", "D"], PeriodRows(lines, "K030", "T02", "K355").Single());
        Assert.Empty(PeriodRows(lines, "K030", "T04", "K355"));
    }

    [Fact]
    public async Task Generate_PartnersAndRevenueInBlockY()
    {
        var lines = await GenerateAsync(await SeedAsync());

        Assert.Equal(["Y540", "12345678000195", "500,00", "0115600"], Of(lines, "Y540").Single());
        var y600 = Of(lines, "Y600");
        Assert.Equal(17, y600[0].Length);
        Assert.Equal(["Y600", "01012026", "", "105", "PF", "52998224725", "ANA SÓCIA", "05", "60,00", "60,00", "", "", "0,00", "0,00",
            "0,00", "0,00", "0,00"], y600[0]);
        Assert.Equal("PJ", y600[1][4]);
    }

    [Fact]
    public async Task Generate_RetificadoraCarriesPreviousHash()
    {
        var db = await SeedAsync(customize: d => d.Context.EcfSettings.Local.Single().PreviousEcfHash = "HASHANTERIOR");
        var request = Request();
        request.Purpose = 1;

        var header = (await GenerateAsync(db, request))[0];

        Assert.Equal("S", header[11]);
        Assert.Equal("HASHANTERIOR", header[12]);
    }

    [Fact]
    public async Task Check_TotalsEqualTheFile()
    {
        var db = await SeedAsync(customize: d => d.Context.EcfAdjustments.Add(
            Adjustment(0, EcfAdjustmentTax.Both, EcfAdjustmentKind.Addition, "93", 100m)));

        var check = await Service(db).CheckAsync(Request());

        Assert.Empty(check.Issues);
        var p = check.Periods.Single();
        Assert.Equal(("A00", 300m, 100m, 400m, 60m, 0m, 400m, 36m), (p.Period, p.BookProfit, p.IrpjAdditions, p.IrpjBase, p.Irpj,
            p.IrpjSurtax, p.CsllBase, p.Csll));
        Assert.Equal(75m, p.IrpjEstimates);
        Assert.Equal(12, check.Estimates.Count);
    }

    // ---- validação ----

    private static async Task<List<string>> BlockingAsync(IUnitOfWork db, EcfRequest? request = null)
    {
        var data = await new EcfDataLoader(db, new EcdDataLoader(db)).LoadAsync(request ?? Request());
        return EcfValidator.Validate(data, request ?? Request()).Where(i => i.Blocking).Select(i => i.Message).ToList();
    }

    [Fact]
    public async Task Validate_CompleteYearHasNoIssues() => Assert.Empty(await BlockingAsync(await SeedAsync()));

    [Fact]
    public async Task Validate_MissingSettingsAndPartnersBlock()
    {
        var db = await SeedAsync(customize: d =>
        {
            d.Context.EcfSettings.RemoveRange(d.Context.EcfSettings.Local.ToList());
            d.Context.EcfPartners.RemoveRange(d.Context.EcfPartners.Local.ToList());
        });

        Assert.Equal(["Parâmetros da ECF não cadastrados para a filial.", "Quadro societário não informado nos parâmetros da ECF."],
            await BlockingAsync(db));
        Assert.Contains("Parâmetros da ECF não cadastrados", (await Assert.ThrowsAsync<BusinessException>(() => Service(db).GenerateAsync(Request()))).Message);
    }

    [Fact]
    public async Task Generate_PartBExclusionAndOffsetAreCredits()
    {
        var db = await SeedAsync(customize: d => d.Context.EcfAdjustments.AddRange(
            Adjustment(0, EcfAdjustmentTax.Irpj, EcfAdjustmentKind.Exclusion, "173", 40m, partB: "B02"),
            Adjustment(0, EcfAdjustmentTax.Irpj, EcfAdjustmentKind.LossOffset, "P", 50m, partB: "B03")));
        var lines = await GenerateAsync(db);

        Assert.Equal([["M305", "B02", "40,00", "C"], ["M305", "B03", "50,00", "C"]], Of(lines, "M305"));
        Assert.Equal(["M500", "B02", "I", "0,00", "D", "40,00", "C", "0,00", "D", "40,00", "C"], Of(lines, "M500")[0]);
        Assert.Equal("C", Of(lines, "M500")[1][10]);
    }

    [Fact]
    public async Task Validate_IncompleteAddressAndNatureBlock()
    {
        var db = await SeedAsync(customize: d =>
        {
            d.Context.Branchs.Local.Single(b => b.Code == "01").District = " ";
            d.Context.EcfSettings.Local.Single().NatureCode = "206";
        });
        Assert.Equal(["Endereço da filial matriz incompleto para a ECF (logradouro, número, bairro e CEP).",
            "Natureza jurídica deve ter 4 dígitos."], await BlockingAsync(db));
    }

    [Fact]
    public async Task Validate_EcdBookDataIsNotRequired_SignerIs()
    {
        var db = await SeedAsync(customize: d =>
        {
            var s = d.Context.EcdSettings.Local.Single();
            (s.Nire, s.ConstitutiveActsDate, s.AccountantCrcSequence, s.SignerQualificationCode, s.SignerCpf) = (null, null, null, "1", "");
        });
        Assert.Equal(["Responsável pela ECF: informe CPF nos Parâmetros da ECD."], await BlockingAsync(db));

        var cutover = await SeedAsync(customize: d => d.Context.AccountingSettings.Local.Single().CutoverDate = new DateTime(2026, 2, 1));
        var messages = await BlockingAsync(cutover);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.DoesNotContain("ECD", m));
    }

    [Fact]
    public async Task Validate_InvalidCnaeBlocks()
    {
        var db = await SeedAsync(customize: d => d.Context.EcfSettings.Local.Single().MainCnae = "01156");
        Assert.Equal(["Parâmetros da ECF: CNAE principal 01156 inválido (7 dígitos)."], await BlockingAsync(db));
    }

    [Fact]
    public async Task Validate_PartnersPercentToleranceIsOneCent()
    {
        var ok = await SeedAsync(customize: d => d.Context.EcfPartners.Local.First().TotalCapitalPercent = 59.99m);
        Assert.Empty(await BlockingAsync(ok));

        var bad = await SeedAsync(customize: d => d.Context.EcfPartners.Local.First().TotalCapitalPercent = 50m);
        Assert.Equal(["Quadro societário deve somar 100% do capital (soma atual 90,00%)."], await BlockingAsync(bad));
    }

    [Fact]
    public async Task Validate_AdjustmentWithoutCodeOrWrongPeriodBlocks()
    {
        var db = await SeedAsync(customize: d => d.Context.EcfAdjustments.AddRange(
            Adjustment(0, EcfAdjustmentTax.Irpj, EcfAdjustmentKind.Addition, " ", 10m),
            Adjustment(2, EcfAdjustmentTax.Irpj, EcfAdjustmentKind.Addition, "93", 10m)));

        var blocking = await BlockingAsync(db);

        Assert.Contains("Ajuste \"Ajuste  \" sem código da tabela dinâmica.", blocking);
        Assert.Contains("Ajuste \"Ajuste 93\" no período 2, incompatível com a apuração anual (período 0).", blocking);
    }

    [Fact]
    public async Task Validate_LossOffsetAboveThirtyPercentBlocks()
    {
        // Lucro 300: limite 90
        var ok = await SeedAsync(customize: d => d.Context.EcfAdjustments.Add(Adjustment(0, EcfAdjustmentTax.Both, EcfAdjustmentKind.LossOffset, "P", 90m)));
        Assert.Empty(await BlockingAsync(ok));

        var bad = await SeedAsync(customize: d => d.Context.EcfAdjustments.Add(Adjustment(0, EcfAdjustmentTax.Csll, EcfAdjustmentKind.LossOffset, "P", 90.01m)));
        Assert.Equal(["Compensação de prejuízo do período A00 acima de 30% do lucro ajustado."], await BlockingAsync(bad));
    }

    [Fact]
    public async Task Validate_RetificadoraWithoutHashBlocks_AndEcdRulesApply()
    {
        var db = await SeedAsync(customize: d => d.Context.AccountingPeriods.Remove(d.Context.AccountingPeriods.Local.Single(p => p.Month == 7)));
        var request = Request();
        request.Purpose = 1;

        Assert.Equal(["Mês 07/2026 não fechado.", "ECF retificadora: informe o hash da ECF anterior nos parâmetros da ECF."],
            await BlockingAsync(db, request));
    }

    // ---- controller ----

    [Fact]
    public async Task Controller_StandaloneOnlyAndGeneratesFile()
    {
        var sap = new EcfController(Service(TestDb.CreateUnitOfWork()), TestConfiguration.Erp("SAPB1"));
        Assert.Equal("Disponível somente no modo STANDALONE.", Assert.IsType<BadRequestObjectResult>(await sap.Generate(Request())).Value);
        Assert.Equal("Disponível somente no modo STANDALONE.", Assert.IsType<BadRequestObjectResult>(await sap.Check(Request())).Value);

        var controller = new EcfController(Service(await SeedAsync()), TestConfiguration.Erp("STANDALONE"));
        var file = Assert.IsType<FileContentResult>(await controller.Generate(Request()));
        Assert.Equal("text/plain; charset=iso-8859-1", file.ContentType);
        Assert.Equal("ECF_12345678000195_2026.txt", file.FileDownloadName);
        Assert.IsType<OkObjectResult>(await controller.Check(Request()));
        Assert.IsType<BadRequestObjectResult>(await controller.Check(new EcfRequest { BranchCode = "02", Year = 2026, Purpose = 2 }));
    }

    [Fact]
    public async Task Controller_BlockingIssuesReturnBadRequest()
    {
        var controller = new EcfController(Service(await SeedAsync(customize: d =>
            d.Context.EcfPartners.RemoveRange(d.Context.EcfPartners.Local.ToList()))), TestConfiguration.Erp("STANDALONE"));

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Generate(Request()));
        Assert.Contains("Quadro societário não informado", (string)bad.Value!);
    }
}
