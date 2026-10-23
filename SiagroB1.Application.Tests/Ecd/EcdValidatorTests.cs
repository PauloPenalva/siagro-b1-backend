using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;
using static SiagroB1.Application.Tests.Ecd.EcdFileServiceTests;

namespace SiagroB1.Application.Tests.Ecd;

public class EcdValidatorTests
{
    private static async Task<EcdData> DataAsync(bool closing = true) =>
        await new EcdDataLoader(await SeedAsync(closing)).LoadAsync(Request());

    private static List<string> Blocking(EcdData data, EcdRequest? request = null) =>
        EcdValidator.Validate(data, request ?? Request()).Where(i => i.Blocking).Select(i => i.Message).ToList();

    [Fact]
    public async Task Validate_CompleteYearHasNoIssues() => Assert.Empty(EcdValidator.Validate(await DataAsync(), Request()));

    [Fact]
    public async Task Validate_YearWithoutEntriesBlocks()
    {
        var data = await DataAsync();
        data.Entries.Clear();
        Assert.Contains("Nenhum lançamento contábil em 2026.", Blocking(data));
    }

    [Fact]
    public async Task Validate_OpenMonthBlocksWithTheMonth()
    {
        var data = await DataAsync();
        data.Periods.Single(p => p.Month == 4).Status = AccountingPeriodStatus.Open;
        Assert.Equal(["Mês 04/2026 não fechado."], Blocking(data));
    }

    [Fact]
    public async Task Validate_MovedAccountWithoutReferentialBlocks_UnusedDoesNot()
    {
        var data = await DataAsync();
        Assert.Null(data.Accounts.Single(a => a.Code == "4.1.99").ReferentialCode);
        data.Accounts.Single(a => a.Code == "1.1.01").ReferentialCode = null;
        Assert.Equal(["Conta 1.1.01 sem código do plano referencial."], Blocking(data));
    }

    [Fact]
    public async Task Validate_UnbalancedEntryBlocks()
    {
        var data = await DataAsync();
        var entry = data.Entries.First(e => e.Lines.Any(l => l.EntryType == JournalEntryLineType.Credit));
        entry.Lines.Single(l => l.EntryType == JournalEntryLineType.Credit).Amount = 150m;
        Assert.Contains($"Lançamento {entry.DocEntry} desbalanceado.", Blocking(data));
    }

    [Fact]
    public async Task Validate_AccountOutsideStatementsBlocks()
    {
        var data = await DataAsync();
        data.BalanceSheet!.Lines.Single(l => l.Code == "A1").AccountTo = "1.0";
        Assert.Equal(["Conta 1.1.01 fora do Balanço e da DRE."], Blocking(data));
    }

    [Fact]
    public async Task Validate_MissingSettingsAccountantAndDeclarantBlock()
    {
        var data = await DataAsync();
        data.Settings = null;
        data.Accountant = null;
        Assert.Equal(["Parâmetros da ECD não cadastrados para a filial.", "Parâmetros do SPED Fiscal não cadastrados para a filial."],
            Blocking(data));

        data.Declarant = null;
        Assert.Equal(["Filiais do CNPJ 12345678 sem matriz (/0001) cadastrada."], Blocking(data));
    }

    [Fact]
    public async Task Validate_IncompleteSettingsBlock()
    {
        var data = await DataAsync();
        data.Settings!.SignerQualificationCode = "777";
        data.Settings.ConstitutiveActsDate = null;
        data.Accountant!.AccountantCrc = "";
        Assert.Equal([
            "Parâmetros da ECD: informe a data do arquivamento dos atos constitutivos.",
            "Parâmetros da ECD: qualificação do assinante 777 fora da tabela da ECD.",
            "Parâmetros do SPED Fiscal: informe CRC do contabilista.",
        ], Blocking(data));
    }

    [Fact]
    public async Task Validate_CutoverMissingOrAfterYearStartBlocks()
    {
        var data = await DataAsync();
        data.Accounting.CutoverDate = new DateTime(2026, 2, 1);
        Assert.Equal(["Data de corte da contabilidade (01/02/2026) depois do início de 2026: a ECD precisa do ano inteiro."], Blocking(data));

        data.Accounting.CutoverDate = null;
        Assert.Equal(["Contabilidade desligada: informe a data de corte nos parâmetros da contabilidade."], Blocking(data));
    }

    [Fact]
    public async Task Validate_AccountWithoutTypeBlocksIncludingParent()
    {
        var data = await DataAsync();
        data.Accounts.Single(a => a.Code == "1").Type = null;
        Assert.Equal(["Conta 1 sem tipo (natureza do grupo)."], Blocking(data));
    }

    [Fact]
    public async Task Validate_SubstituteWithoutHashAndOldYearBlock()
    {
        var data = await DataAsync();
        Assert.Contains("Escrituração substituta: informe o hash do livro substituído.",
            Blocking(data, new EcdRequest { BranchCode = "02", Year = 2026, Purpose = 1 }));
        Assert.Contains("ECD disponível a partir do ano-calendário 2020 (leiaute 9).",
            Blocking(data, new EcdRequest { BranchCode = "02", Year = 2019 }));
    }

    [Fact]
    public async Task Validate_MissingViewAndUnknownCostCenterBlock()
    {
        var data = await DataAsync();
        data.IncomeStatement = null;
        data.CostCenters.RemoveAll(c => c.Code == "CC01");
        var blocking = Blocking(data);
        Assert.Contains("Visão gerencial da DRE não cadastrada.", blocking);
        Assert.Contains("Centro de custo CC01 não cadastrado.", blocking);
    }

    [Fact]
    public async Task Validate_InactiveAccountAndUnbalancedSheetWarn()
    {
        var data = await DataAsync();
        data.Accounts.Single(a => a.Code == "1.1.01").Inactive = true;
        data.BalanceSheet!.Lines.Add(new ManagementViewLine
            { Order = 5, Code = "X", ParentCode = "A", Kind = ManagementViewLineKind.Accounts, AccountFrom = "2.3.01", AccountTo = "2.3.01" });

        var warnings = EcdValidator.Validate(data, Request()).Where(i => !i.Blocking).Select(i => i.Message).ToList();

        Assert.Equal(["Conta 1.1.01 inativa com movimento em 2026.", "Balanço: ativo difere do passivo + PL em 1.000,00."], warnings);
    }

    // ---- controller ----

    [Fact]
    public async Task Controller_NonStandaloneReturnsBadRequest()
    {
        var controller = new EcdController(Service(TestDb.CreateUnitOfWork()), TestConfiguration.Erp("SAPB1"));

        Assert.Equal("Disponível somente no modo STANDALONE.", Assert.IsType<BadRequestObjectResult>(await controller.Generate(Request())).Value);
        Assert.Equal("Disponível somente no modo STANDALONE.", Assert.IsType<BadRequestObjectResult>(await controller.Check(Request())).Value);
    }

    [Fact]
    public async Task Controller_GeneratesLatin1TextFile()
    {
        var controller = new EcdController(Service(await SeedAsync()), TestConfiguration.Erp("STANDALONE"));

        var file = Assert.IsType<FileContentResult>(await controller.Generate(Request()));

        Assert.Equal("text/plain; charset=iso-8859-1", file.ContentType);
        Assert.Equal("ECD_12345678000195_2026.txt", file.FileDownloadName);
    }

    [Fact]
    public async Task Controller_BlockingIssuesAndInvalidRequestReturnBadRequest()
    {
        var controller = new EcdController(Service(await SeedAsync(customize: db =>
            db.Context.EcdSettings.RemoveRange(db.Context.EcdSettings.Local.ToList()))), TestConfiguration.Erp("STANDALONE"));

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.Generate(Request()));
        Assert.Contains("Parâmetros da ECD não cadastrados", (string)bad.Value!);
        Assert.IsType<BadRequestObjectResult>(await controller.Check(new EcdRequest { BranchCode = "02", Year = 2026, Purpose = 2 }));
        Assert.IsType<OkObjectResult>(await controller.Check(Request()));
    }
}
