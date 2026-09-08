using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountsCreateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountsCreateService Service() => new(_db);

    private static FinancialAccount Form(
        string code = "CX01",
        string name = "Caixa Geral",
        FinancialAccountType type = FinancialAccountType.Cash,
        string? bankAccountNumber = null,
        string? bankCode = null) => new()
    {
        Code = code,
        Name = name,
        Type = type,
        BankAccountNumber = bankAccountNumber,
        BankCode = bankCode
    };

    [Fact]
    public async Task A_cash_account_is_created_with_the_default_currency()
    {
        var account = await Service().ExecuteAsync(Form(), "tester");

        Assert.Equal("CX01", account.Code);
        Assert.Equal(CurrencyType.Brl, account.Currency);
        Assert.False(account.Inactive);
    }

    [Fact]
    public async Task Refuses_a_bank_account_without_the_account_number()
    {
        var form = Form(code: "BB01", name: "Banco do Brasil", type: FinancialAccountType.Bank,
                        bankCode: "001");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(form, "tester"));

        Assert.Contains("conta", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_bank_account_without_the_bank_code()
    {
        var form = Form(code: "BB02", name: "Banco do Brasil", type: FinancialAccountType.Bank,
                        bankAccountNumber: "12345-6");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(form, "tester"));

        Assert.Contains("banco", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_duplicated_code()
    {
        await Service().ExecuteAsync(Form(), "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Form(name: "Outro caixa"), "tester"));

        Assert.Contains("já existe", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
