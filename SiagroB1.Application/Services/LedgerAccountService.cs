using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>
/// Contas contábeis mantidas na tabela local LEDGER_ACCOUNTS (modo STANDALONE).
/// </summary>
public class LedgerAccountService(IUnitOfWork db, ILogger<LedgerAccountService> logger)
    : ILedgerAccountService
{
    public IQueryable<LedgerAccountModel> QueryAll()
    {
        return db.Context.LedgerAccounts
            .Select(x => new LedgerAccountModel()
            {
                Code = x.Code,
                Name = x.Name,
                Type = x.Type,
                AllowsPosting = x.AllowsPosting,
                Inactive = x.Inactive,
                AcceptsCostCenter = x.AcceptsCostCenter,
                RequiresCostCenter = x.RequiresCostCenter,
                AcceptsAccountingItem = x.AcceptsAccountingItem,
                RequiresAccountingItem = x.RequiresAccountingItem,
                ParentCode = x.ParentCode,
                Nature = x.Nature,
                ResultClosingAccountCode = x.ResultClosingAccountCode,
                ReferentialCode = x.ReferentialCode,
                BlockedFrom = x.BlockedFrom,
                BlockedTo = x.BlockedTo,
            })
            .AsNoTracking();
    }

    public async Task<LedgerAccountModel?> GetByIdAsync(string code)
    {
        try
        {
            return await QueryAll().FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<IEnumerable<LedgerAccountModel>> GetAllAsync()
    {
        return await QueryAll().ToListAsync();
    }

    public async Task<LedgerAccountModel> CreateAsync(LedgerAccountModel entity)
    {
        EnsureTypeInformed(entity);
        EnsureFlagsConsistent(entity);
        await EnsureAccountingFieldsAsync(entity.Code, entity);

        if (await db.Context.LedgerAccounts.AnyAsync(x => x.Code == entity.Code))
        {
            throw new DefaultException($"Conta contábil {entity.Code} já cadastrada.");
        }

        var ledgerAccount = new LedgerAccount()
        {
            Code = entity.Code,
            Name = entity.Name,
            Type = entity.Type,
            AllowsPosting = entity.AllowsPosting,
            Inactive = entity.Inactive,
            AcceptsCostCenter = entity.AcceptsCostCenter,
            RequiresCostCenter = entity.RequiresCostCenter,
            AcceptsAccountingItem = entity.AcceptsAccountingItem,
            RequiresAccountingItem = entity.RequiresAccountingItem,
        };
        ApplyAccountingFields(ledgerAccount, entity);

        await db.Context.LedgerAccounts.AddAsync(ledgerAccount);
        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<LedgerAccountModel?> UpdateAsync(string code, LedgerAccountModel entity)
    {
        EnsureTypeInformed(entity);
        EnsureFlagsConsistent(entity);

        var ledgerAccount = await db.Context.LedgerAccounts.FirstOrDefaultAsync(x => x.Code == code);

        if (ledgerAccount == null)
        {
            return null;
        }

        await EnsureAccountingFieldsAsync(code, entity);

        ledgerAccount.Name = entity.Name;
        ledgerAccount.Type = entity.Type;
        ledgerAccount.AllowsPosting = entity.AllowsPosting;
        ledgerAccount.Inactive = entity.Inactive;
        ledgerAccount.AcceptsCostCenter = entity.AcceptsCostCenter;
        ledgerAccount.RequiresCostCenter = entity.RequiresCostCenter;
        ledgerAccount.AcceptsAccountingItem = entity.AcceptsAccountingItem;
        ledgerAccount.RequiresAccountingItem = entity.RequiresAccountingItem;
        ApplyAccountingFields(ledgerAccount, entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(code))
            {
                throw new KeyNotFoundException("Entity not found.");
            }

            throw new DefaultException("Error updating entity.");
        }

        return new LedgerAccountModel()
        {
            Code = ledgerAccount.Code,
            Name = ledgerAccount.Name,
            Type = ledgerAccount.Type,
            AllowsPosting = ledgerAccount.AllowsPosting,
            Inactive = ledgerAccount.Inactive,
            AcceptsCostCenter = ledgerAccount.AcceptsCostCenter,
            RequiresCostCenter = ledgerAccount.RequiresCostCenter,
            AcceptsAccountingItem = ledgerAccount.AcceptsAccountingItem,
            RequiresAccountingItem = ledgerAccount.RequiresAccountingItem,
            ParentCode = ledgerAccount.ParentCode,
            Nature = ledgerAccount.Nature,
            ResultClosingAccountCode = ledgerAccount.ResultClosingAccountCode,
            ReferentialCode = ledgerAccount.ReferentialCode,
            BlockedFrom = ledgerAccount.BlockedFrom,
            BlockedTo = ledgerAccount.BlockedTo,
        };
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var ledgerAccount = await db.Context.LedgerAccounts.FirstOrDefaultAsync(x => x.Code == code);

        if (ledgerAccount == null)
        {
            return false;
        }

        db.Context.LedgerAccounts.Remove(ledgerAccount);
        await db.SaveChangesAsync();

        return true;
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<LedgerAccountModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// A coluna é anulável no banco por causa do modo SAPB1 (o SAP não informa o tipo),
    /// então a obrigatoriedade do cadastro local é validada aqui.
    /// </summary>
    private static void EnsureTypeInformed(LedgerAccountModel entity)
    {
        if (entity.Type == null)
        {
            throw new DefaultException("Informe o tipo da conta contábil.");
        }
    }

    /// <summary>Uma conta não pode exigir o que não aceita.</summary>
    private static void EnsureFlagsConsistent(LedgerAccountModel entity)
    {
        if (entity.RequiresCostCenter && !entity.AcceptsCostCenter)
        {
            throw new DefaultException("A conta não pode exigir e não aceitar centro de custo.");
        }

        if (entity.RequiresAccountingItem && !entity.AcceptsAccountingItem)
        {
            throw new DefaultException("A conta não pode exigir e não aceitar item contábil.");
        }
    }

    private static void ApplyAccountingFields(LedgerAccount target, LedgerAccountModel source)
    {
        target.ParentCode = string.IsNullOrWhiteSpace(source.ParentCode) ? null : source.ParentCode;
        target.Nature = source.Nature ?? LedgerAccountRules.DefaultNature(source.Type!.Value);
        target.ResultClosingAccountCode =
            string.IsNullOrWhiteSpace(source.ResultClosingAccountCode) ? null : source.ResultClosingAccountCode;
        target.ReferentialCode = string.IsNullOrWhiteSpace(source.ReferentialCode) ? null : source.ReferentialCode;
        target.BlockedFrom = source.BlockedFrom;
        target.BlockedTo = source.BlockedTo;
    }

    /// <summary>Hierarquia (pai sintético, sem ciclo), classe x filhas e conta de apuração.</summary>
    private async Task EnsureAccountingFieldsAsync(string code, LedgerAccountModel entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ResultClosingAccountCode) &&
            !LedgerAccountRules.IsResultAccount(entity.Type))
        {
            throw new DefaultException("Conta de apuração só se aplica a contas de receita ou despesa.");
        }

        if (entity.AllowsPosting && await db.Context.LedgerAccounts.AnyAsync(x => x.ParentCode == code))
        {
            throw new DefaultException($"A conta {code} tem contas inferiores e precisa continuar sintética.");
        }

        if (string.IsNullOrWhiteSpace(entity.ParentCode)) return;

        // Sobe a cadeia a partir do pai; se reencontrar a própria conta, haveria ciclo.
        var current = entity.ParentCode;
        var visited = new HashSet<string>();
        var first = true;
        while (current != null && visited.Add(current))
        {
            if (current == code)
            {
                throw new DefaultException($"A conta superior {entity.ParentCode} geraria um ciclo na hierarquia.");
            }

            var parent = await db.Context.LedgerAccounts.AsNoTracking()
                .Where(x => x.Code == current)
                .Select(x => new { x.AllowsPosting, x.ParentCode })
                .FirstOrDefaultAsync();
            if (parent == null)
            {
                if (first) throw new DefaultException($"Conta superior {current} não encontrada.");
                break;
            }

            if (first && parent.AllowsPosting)
            {
                throw new DefaultException($"A conta superior {current} precisa ser sintética.");
            }

            first = false;
            current = parent.ParentCode;
        }
    }

    private bool EntityExists(string code)
    {
        return db.Context.LedgerAccounts.Any(x => x.Code == code);
    }
}
