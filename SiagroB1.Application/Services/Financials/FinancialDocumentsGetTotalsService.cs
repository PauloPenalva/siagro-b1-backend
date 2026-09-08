using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialDocumentsGetTotalsService(IUnitOfWork db)
{
    public async Task<FinancialDocumentTotalsDto> ExecuteAsync(string direction, string? branchCode)
    {
        if (!Enum.TryParse<FinancialDirection>(direction, ignoreCase: true, out var parsed))
            throw new ApplicationException("Direção inválida. Informe Payable ou Receivable.");

        var today = DateTime.Today;

        var query = db.Context.FinancialDocuments.AsNoTracking()
            .Where(x => x.Direction == parsed && x.Status != FinancialDocumentStatus.Canceled);

        if (!string.IsNullOrWhiteSpace(branchCode))
            query = query.Where(x => x.BranchCode == branchCode);

        var rows = await query
            .Select(x => new { x.NetAmount, x.SettledAmount, x.DueDate })
            .ToListAsync();

        var open = rows.Select(r => new { Open = r.NetAmount - r.SettledAmount, r.DueDate })
                       .Where(r => r.Open > 0)
                       .ToList();

        return new FinancialDocumentTotalsDto
        {
            OpenAmount = decimal.Round(open.Sum(r => r.Open), 2, MidpointRounding.ToEven),
            OverdueAmount = decimal.Round(
                open.Where(r => r.DueDate.Date < today).Sum(r => r.Open), 2, MidpointRounding.ToEven),
            DueAmount = decimal.Round(
                open.Where(r => r.DueDate.Date >= today).Sum(r => r.Open), 2, MidpointRounding.ToEven),
            DocumentCount = open.Count
        };
    }
}
