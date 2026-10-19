using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Lançamento manual na conta financeira (tarifa, depósito, rendimento, saldo inicial...).</summary>
public class FinancialAccountEntriesCreateService(IUnitOfWork db)
{
    public async Task<FinancialAccountEntry> ExecuteAsync(FinancialAccountEntry entry, string userName)
    {
        var code = entry.FinancialAccountCode?.Trim().ToUpperInvariant() ?? string.Empty;

        var account = await db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code)
                      ?? throw new ApplicationException($"Conta financeira {code} não encontrada.");

        if (account.Inactive)
            throw new ApplicationException("A conta financeira está inativa.");

        var amount = decimal.Round(entry.Amount, 2, MidpointRounding.ToEven);
        if (amount == 0m)
            throw new ApplicationException("Informe um valor diferente de zero.");

        if (string.IsNullOrWhiteSpace(entry.Description))
            throw new ApplicationException("Informe a descrição do lançamento.");

        var created = new FinancialAccountEntry
        {
            FinancialAccountCode = code,
            EntryDate = entry.EntryDate.Date,
            Amount = amount,
            Description = entry.Description.Trim(),
            DocumentReference = string.IsNullOrWhiteSpace(entry.DocumentReference) ? null : entry.DocumentReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim(),
            CreatedBy = userName,
            UpdatedBy = userName
        };

        db.Context.FinancialAccountEntries.Add(created);
        await db.SaveChangesAsync();
        return created;
    }
}
