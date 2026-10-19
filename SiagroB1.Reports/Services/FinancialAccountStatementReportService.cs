using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Extrato da conta financeira: saldo anterior, baixas e lançamentos manuais do período com saldo acumulado e a marca
/// de conciliado. Mesma consulta da tela de lançamentos (FinancialAccountMovementsQuery), nos dois modos de ERP.
/// </summary>
public class FinancialAccountStatementReportService(IUnitOfWork db, IFastReportService reportService)
{
    public static string? Validate(FinancialAccountStatementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AccountCode))
            return "Informe a conta financeira.";

        if (request.DateFrom.Date > request.DateTo.Date)
            return "A data inicial deve ser menor ou igual à final.";

        return null;
    }

    public async Task<byte[]> ExecuteAsync(FinancialAccountStatementRequest request)
    {
        if (Validate(request) is { } error)
            throw new ApplicationException(error);

        var code = request.AccountCode!.Trim().ToUpperInvariant();
        var account = await db.Context.FinancialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code)
                      ?? throw new ApplicationException($"Conta financeira {code} não encontrada.");

        var statement = await FinancialAccountMovementsQuery.StatementAsync(
            db.Context, code, request.DateFrom, request.DateTo, request.OnlyReconciled, CancellationToken.None);

        var rows = statement.Rows.Select(r => new FinancialAccountStatementRowDto
        {
            Date = r.Date.ToString("dd/MM/yyyy"),
            Description = r.Description,
            DocumentReference = r.DocumentReference ?? string.Empty,
            Partner = r.CardName ?? r.CardCode ?? string.Empty,
            In = r.Amount > 0 ? r.Amount : 0m,
            Out = r.Amount < 0 ? -r.Amount : 0m,
            Balance = r.RunningBalance,
            Reconciled = r.Reconciled ? "Sim" : "Não"
        }).ToList();

        var bank = new List<string>();
        if (!string.IsNullOrWhiteSpace(account.BankCode) || !string.IsNullOrWhiteSpace(account.BankName))
            bank.Add($"Banco {account.BankCode} {account.BankName}".Trim());
        if (!string.IsNullOrWhiteSpace(account.BankBranch)) bank.Add($"Agência {account.BankBranch}");
        if (!string.IsNullOrWhiteSpace(account.BankAccountNumber)) bank.Add($"Conta {account.BankAccountNumber}");

        var period = $"Período: {request.DateFrom:dd/MM/yyyy} a {request.DateTo:dd/MM/yyyy}";
        if (request.OnlyReconciled) period += " · somente conciliados";

        var parameters = new Dictionary<string, object>
        {
            ["pAccount"] = $"{account.Code} - {account.Name}",
            ["pBankInfo"] = string.Join(" · ", bank),
            ["pPeriod"] = period,
            ["pOpeningBalance"] = statement.OpeningBalance,
            ["pClosingBalance"] = statement.ClosingBalance,
            ["pTotalIn"] = statement.TotalIn,
            ["pTotalOut"] = statement.TotalOut,
            ["pRowCount"] = rows.Count
        };

        return await reportService.GeneratePdfAsync(
            "FinancialAccountStatement.frx", rows, "StatementRows", "StatementRows", parameters);
    }
}
