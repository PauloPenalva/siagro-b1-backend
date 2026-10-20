using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Transferência entre contas financeiras: grava a transferência e as duas linhas (saída na origem, entrada no destino)
/// na mesma transação, ambas na data da transferência. Moeda igual é obrigatória; filiais podem diferir.
/// </summary>
public class FinancialAccountTransfersCreateService(IUnitOfWork db)
{
    public async Task<FinancialAccountTransfer> ExecuteAsync(
        FinancialAccountTransfer transfer, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var fromCode = transfer.FromAccountCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var toCode = transfer.ToAccountCode?.Trim().ToUpperInvariant() ?? string.Empty;

        if (fromCode.Length == 0 || toCode.Length == 0)
            throw new ApplicationException("Informe a conta de origem e a de destino.");

        if (fromCode == toCode)
            throw new ApplicationException("A conta de origem e a de destino precisam ser diferentes.");

        var amount = decimal.Round(transfer.Amount, 2, MidpointRounding.ToEven);
        if (amount <= 0m)
            throw new ApplicationException("O valor da transferência deve ser maior que zero.");

        var from = await db.Context.FinancialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == fromCode)
                   ?? throw new ApplicationException($"Conta financeira {fromCode} não encontrada.");
        var to = await db.Context.FinancialAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == toCode)
                 ?? throw new ApplicationException($"Conta financeira {toCode} não encontrada.");

        if (from.Inactive) throw new ApplicationException($"A conta financeira {fromCode} está inativa.");
        if (to.Inactive) throw new ApplicationException($"A conta financeira {toCode} está inativa.");

        if (from.Currency != to.Currency)
            throw new ApplicationException(
                $"As contas {fromCode} ({from.Currency}) e {toCode} ({to.Currency}) precisam ser da mesma moeda.");

        var description = transfer.Description?.Trim() ?? string.Empty;
        var suffix = description.Length > 0 ? " - " + description : string.Empty;
        var date = transfer.TransferDate.Date;
        var key = Guid.NewGuid();

        var created = new FinancialAccountTransfer
        {
            Key = key,
            FromAccountCode = fromCode,
            ToAccountCode = toCode,
            TransferDate = date,
            Amount = amount,
            Description = description,
            Notes = string.IsNullOrWhiteSpace(transfer.Notes) ? null : transfer.Notes.Trim(),
            CreatedBy = userName,
            UpdatedBy = userName
        };

        FinancialAccountEntry Leg(string code, decimal signed, string text) => new()
        {
            FinancialAccountCode = code,
            EntryDate = date,
            Amount = signed,
            Description = text + suffix,
            TransferKey = key,
            CreatedBy = userName,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialAccountTransfers.Add(created);
            db.Context.FinancialAccountEntries.Add(Leg(fromCode, -amount, $"Transferência para {toCode}"));
            db.Context.FinancialAccountEntries.Add(Leg(toCode, amount, $"Transferência de {fromCode}"));
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }

        return created;
    }
}
