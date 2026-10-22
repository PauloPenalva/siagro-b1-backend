using FastReport;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Accounting;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Diário (CTBR110): lançamentos do período em ordem de data, lote, documento e linha, com termos de abertura e
/// encerramento. A numeração das folhas começa na folha inicial informada (o livro continua o anterior).
/// </summary>
public class JournalReportService(IUnitOfWork db, FastReportService reportService)
{
    public static string? Validate(JournalReportRequest request) =>
        request.DateFrom.Date > request.DateTo.Date ? "A data inicial deve ser menor ou igual à final."
        : request.StartPage < 1 ? "A folha inicial deve ser maior que zero."
        : request.BookNumber < 1 ? "O número do livro deve ser maior que zero."
        : null;

    public async Task<List<JournalRowDto>> RowsAsync(JournalReportRequest request)
    {
        if (Validate(request) is { } error) throw new ApplicationException(error);

        var postings = await new AccountPostingReader(db)
            .ReadAsync(request.DateFrom, request.DateTo, new AccountPostingFilter(request.BranchCode));
        var subBatches = await db.Context.JournalEntries.AsNoTracking()
            .Where(e => e.PostingDate >= request.DateFrom.Date && e.PostingDate <= request.DateTo.Date)
            .ToDictionaryAsync(e => e.DocEntry, e => e.SubBatch);
        var names = await db.Context.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a.Name);

        return postings
            .OrderBy(p => p.Date).ThenBy(p => p.Batch, StringComparer.Ordinal)
            .ThenBy(p => subBatches.GetValueOrDefault(p.DocEntry), StringComparer.Ordinal)
            .ThenBy(p => p.DocumentNumber, StringComparer.Ordinal).ThenBy(p => p.DocEntry)
            .ThenBy(p => p.LineNumber).ThenBy(p => p.Debit > 0 ? 0 : 1)
            .Select(p => new JournalRowDto
            {
                Date = p.Date,
                Batch = p.Batch,
                SubBatch = subBatches.GetValueOrDefault(p.DocEntry) ?? string.Empty,
                DocumentNumber = p.DocumentNumber,
                LineNumber = p.LineNumber,
                BranchCode = p.BranchCode,
                Account = p.AccountCode,
                AccountName = names.GetValueOrDefault(p.AccountCode) ?? string.Empty,
                Debit = p.Debit,
                Credit = p.Credit,
                History = p.History,
                CostCenter = p.CostCenterCode ?? string.Empty,
                Item = p.AccountingItemCode ?? string.Empty,
            }).ToList();
    }

    public async Task<byte[]> ExecuteAsync(JournalReportRequest request)
    {
        using var report = await PrepareJournalAsync(request);
        return FastReportService.ExportPdf(report, "Livro Diário");
    }

    /// <summary>Prepara o livro sem exportar (as folhas e os termos são conferidos nos testes).</summary>
    public async Task<Report> PrepareJournalAsync(JournalReportRequest request)
    {
        var rows = await RowsAsync(request);
        var branch = await db.Context.Branchs.AsNoTracking()
            .Where(b => string.IsNullOrWhiteSpace(request.BranchCode) || b.Code == request.BranchCode)
            .OrderBy(b => b.Code)
            .Select(b => new { b.Code, b.LegalName, b.BranchName, b.TaxId })
            .FirstOrDefaultAsync();
        var parameters = new Dictionary<string, object>
        {
            ["pPeriod"] = $"Período: {request.DateFrom:dd/MM/yyyy} a {request.DateTo:dd/MM/yyyy}",
            ["pFilters"] = AccountingReportFilters.Describe(request.BranchCode, null, null, null, null),
            ["pLegalName"] = branch?.LegalName ?? branch?.BranchName ?? string.Empty,
            ["pTaxId"] = branch?.TaxId ?? string.Empty,
            ["pBookNumber"] = request.BookNumber,
            ["pStartPage"] = request.StartPage,
            ["pTotalDebit"] = rows.Sum(r => r.Debit),
            ["pTotalCredit"] = rows.Sum(r => r.Credit),
        };
        return await reportService.PrepareAsync("AccountingJournal.frx", rows, "JournalRows", "JournalRows", parameters)
               ?? throw new InvalidOperationException("O FastReport não preparou o Diário.");
    }
}
