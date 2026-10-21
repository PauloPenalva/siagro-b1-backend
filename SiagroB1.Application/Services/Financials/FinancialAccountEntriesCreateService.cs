using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Dimensions;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>Lançamento manual na conta financeira (tarifa, depósito, rendimento, saldo inicial...).</summary>
public class FinancialAccountEntriesCreateService(IUnitOfWork db, DimensionCodeValidator? dimensionValidator = null)
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

        // Lançamento nasce pronto: só valida (sem parceiro nem natureza para herdar).
        var dimensions = DimensionSnapshot.From(entry);
        if (dimensionValidator is not null)
            await dimensionValidator.NormalizeAndValidateAsync(dimensions);

        var created = new FinancialAccountEntry
        {
            FinancialAccountCode = code,
            EntryDate = entry.EntryDate.Date,
            Amount = amount,
            Description = entry.Description.Trim(),
            DocumentReference = string.IsNullOrWhiteSpace(entry.DocumentReference) ? null : entry.DocumentReference.Trim(),
            Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim(),
            Dim1Code = dimensions.Dim1Code,
            Dim2Code = dimensions.Dim2Code,
            Dim3Code = dimensions.Dim3Code,
            Dim4Code = dimensions.Dim4Code,
            Dim5Code = dimensions.Dim5Code,
            LedgerAccountCode = dimensions.LedgerAccountCode,
            CreatedBy = userName,
            UpdatedBy = userName
        };

        db.Context.FinancialAccountEntries.Add(created);
        await db.SaveChangesAsync();
        return created;
    }
}
