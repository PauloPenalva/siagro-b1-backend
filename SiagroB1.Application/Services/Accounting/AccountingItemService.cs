using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Itens contábeis (CTD) mantidos na tabela local ACCOUNTING_ITEMS (modo STANDALONE).
/// </summary>
public class AccountingItemService(IUnitOfWork db, ILogger<AccountingItemService> logger)
    : IAccountingItemService
{
    public IQueryable<AccountingItemModel> QueryAll()
    {
        return db.Context.AccountingItems
            .Select(x => new AccountingItemModel()
            {
                Code = x.Code,
                Name = x.Name,
                Inactive = x.Inactive,
                Class = x.Class,
                ParentCode = x.ParentCode,
                ValidFrom = x.ValidFrom,
                ValidTo = x.ValidTo,
            })
            .AsNoTracking();
    }

    public async Task<AccountingItemModel?> GetByIdAsync(string code)
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

    public async Task<IEnumerable<AccountingItemModel>> GetAllAsync()
    {
        return await QueryAll().ToListAsync();
    }

    public async Task<AccountingItemModel> CreateAsync(AccountingItemModel entity)
    {
        var code = AccountingEntityRules.NormalizeCode(entity.Code);
        AccountingEntityRules.ValidateCode(code);
        var parentCode = AccountingEntityRules.NormalizeOptionalCode(entity.ParentCode);

        if (await db.Context.AccountingItems.AnyAsync(x => x.Code.ToUpper() == code))
        {
            throw new DefaultException($"Item contábil {code} já cadastrado.");
        }

        await ValidateAsync(code, entity.Class, parentCode, entity.ValidFrom, entity.ValidTo);

        var item = new AccountingItem()
        {
            Code = code,
            Name = entity.Name,
            Inactive = entity.Inactive,
            Class = entity.Class,
            ParentCode = parentCode,
            ValidFrom = entity.ValidFrom,
            ValidTo = entity.ValidTo,
        };

        await db.Context.AccountingItems.AddAsync(item);
        await db.SaveChangesAsync();

        entity.Code = code;
        entity.ParentCode = parentCode;

        return entity;
    }

    public async Task<AccountingItemModel?> UpdateAsync(string code, AccountingItemModel entity)
    {
        var item = await db.Context.AccountingItems.FirstOrDefaultAsync(x => x.Code == code);

        if (item == null)
        {
            return null;
        }

        var parentCode = AccountingEntityRules.NormalizeOptionalCode(entity.ParentCode);
        await ValidateAsync(item.Code, entity.Class, parentCode, entity.ValidFrom, entity.ValidTo);

        item.Name = entity.Name;
        item.Inactive = entity.Inactive;
        item.Class = entity.Class;
        item.ParentCode = parentCode;
        item.ValidFrom = entity.ValidFrom;
        item.ValidTo = entity.ValidTo;

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

        return new AccountingItemModel()
        {
            Code = item.Code,
            Name = item.Name,
            Inactive = item.Inactive,
            Class = item.Class,
            ParentCode = item.ParentCode,
            ValidFrom = item.ValidFrom,
            ValidTo = item.ValidTo,
        };
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var item = await db.Context.AccountingItems.FirstOrDefaultAsync(x => x.Code == code);

        if (item == null)
        {
            return false;
        }

        await AccountingEntityRules.EnsureNoChildrenAsync(db.Context.AccountingItems, item.Code, x => x.ParentCode);

        db.Context.AccountingItems.Remove(item);
        await db.SaveChangesAsync();

        return true;
    }

    private async Task ValidateAsync(
        string code, AccountingEntityClass cls, string? parentCode, DateTime? validFrom, DateTime? validTo)
    {
        AccountingEntityRules.ValidateValidity(validFrom, validTo);
        await AccountingEntityRules.ValidateAsync(
            db.Context.AccountingItems, code, cls, parentCode, x => x.ParentCode, x => x.Class, x => x.Code);
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<AccountingItemModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    private bool EntityExists(string code)
    {
        return db.Context.AccountingItems.Any(x => x.Code == code);
    }
}
