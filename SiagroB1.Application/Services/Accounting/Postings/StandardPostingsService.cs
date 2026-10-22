using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting.Postings;

public interface IStandardPostingsService
{
    IQueryable<StandardPosting> QueryAll();

    /// <summary>Grava o lançamento padrão do código, substituindo as sequências.</summary>
    Task<StandardPosting> SaveAsync(StandardPosting posting, string user);
}

/// <summary>Cadastro do lançamento padrão (CTBA080): valida as sequências contra o catálogo do código.</summary>
public class StandardPostingsService(IUnitOfWork db) : IStandardPostingsService
{
    public IQueryable<StandardPosting> QueryAll() => db.Context.StandardPostings.AsNoTracking();

    public async Task<StandardPosting> SaveAsync(StandardPosting posting, string user)
    {
        var code = posting.Code?.Trim() ?? string.Empty;
        var definition = StandardPostingCatalog.Find(code)
                         ?? throw new DefaultException($"Código de lançamento padrão {code} não existe.");

        var sequences = new HashSet<string>();
        foreach (var line in posting.Lines)
        {
            line.Sequence = line.Sequence?.Trim() ?? string.Empty;
            if (!sequences.Add(line.Sequence)) throw new DefaultException($"Sequência {line.Sequence} repetida.");
            await ValidateLineAsync(definition, line);
        }

        var row = await db.Context.StandardPostings.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Code == code);
        if (row is null)
        {
            row = new StandardPosting { Code = code };
            db.Context.StandardPostings.Add(row);
        }
        else
        {
            db.Context.StandardPostingLines.RemoveRange(row.Lines);
            row.Lines.Clear();
        }

        row.Active = posting.Active;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = user;
        foreach (var line in posting.Lines.OrderBy(l => l.Sequence, StringComparer.Ordinal))
        {
            line.Id = 0;
            line.Code = code;
            row.Lines.Add(line);
        }

        await db.SaveChangesAsync();
        return row;
    }

    private async Task ValidateLineAsync(StandardPostingDefinition definition, StandardPostingLine line)
    {
        var seq = $"Seq. {line.Sequence}";
        if (!definition.ValueSources.Contains(line.ValueSource))
            throw new DefaultException($"{seq}: fonte de valor {line.ValueSource} não se aplica ao código {definition.Code}.");

        var hasDebit = !string.IsNullOrWhiteSpace(line.DebitAccountSource);
        var hasCredit = !string.IsNullOrWhiteSpace(line.CreditAccountSource);
        switch (line.EntryType)
        {
            case JournalEntryLineType.Double when !hasDebit || !hasCredit:
                throw new DefaultException($"{seq}: informe a origem das contas de débito e de crédito.");
            case JournalEntryLineType.Debit when !hasDebit:
                throw new DefaultException($"{seq}: informe a origem da conta de débito.");
            case JournalEntryLineType.Debit when hasCredit:
                throw new DefaultException($"{seq}: linha de débito não tem conta de crédito.");
            case JournalEntryLineType.Credit when !hasCredit:
                throw new DefaultException($"{seq}: informe a origem da conta de crédito.");
            case JournalEntryLineType.Credit when hasDebit:
                throw new DefaultException($"{seq}: linha de crédito não tem conta de débito.");
            case JournalEntryLineType.HistoryContinuation:
                throw new DefaultException($"{seq}: tipo de linha inválido.");
        }

        if (hasDebit) await ValidateAccountAsync(definition, seq, "débito", line.DebitAccountSource!, line.DebitAccountCode);
        if (hasCredit) await ValidateAccountAsync(definition, seq, "crédito", line.CreditAccountSource!, line.CreditAccountCode);

        RequireFixedCode(seq, line.DebitCostCenterSource, line.DebitCostCenterCode, "o centro de custo fixo de débito");
        RequireFixedCode(seq, line.CreditCostCenterSource, line.CreditCostCenterCode, "o centro de custo fixo de crédito");
        RequireFixedCode(seq, line.DebitItemSource, line.DebitItemCode, "o item contábil fixo de débito");
        RequireFixedCode(seq, line.CreditItemSource, line.CreditItemCode, "o item contábil fixo de crédito");
    }

    private async Task ValidateAccountAsync(StandardPostingDefinition definition, string seq, string side, string source,
        string? fixedCode)
    {
        if (!definition.AccountSources.Contains(source))
            throw new DefaultException($"{seq}: fonte de conta {source} não se aplica ao código {definition.Code}.");

        // A conta fixa também é o último recurso das demais fontes; quando a fonte é Fixed ela é obrigatória.
        if (string.IsNullOrWhiteSpace(fixedCode))
        {
            if (source == StandardPostingCatalog.Fixed) throw new DefaultException($"{seq}: informe a conta fixa de {side}.");
            return;
        }

        var code = fixedCode.Trim();
        if (!await db.Context.LedgerAccounts.AnyAsync(a => a.Code == code && a.AllowsPosting && !a.Inactive))
            throw new DefaultException($"{seq}: a conta fixa de {side} {code} não existe ou é sintética.");
    }

    private static void RequireFixedCode(string seq, DimensionSource source, string? code, string what)
    {
        if (source == DimensionSource.Fixed && string.IsNullOrWhiteSpace(code))
            throw new DefaultException($"{seq}: informe {what}.");
    }
}
