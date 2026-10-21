using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Accounting;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>
/// Centros de custo mantidos na tabela local COST_CENTERS (modo STANDALONE).
/// </summary>
public class CostCenterService(IUnitOfWork db, ILogger<CostCenterService> logger)
    : ICostCenterService
{
    public IQueryable<CostCenterModel> QueryAll()
    {
        return db.Context.CostCenters
            .Select(x => new CostCenterModel()
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

    public async Task<CostCenterModel?> GetByIdAsync(string code)
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

    public async Task<IEnumerable<CostCenterModel>> GetAllAsync()
    {
        return await QueryAll().ToListAsync();
    }

    public async Task<CostCenterModel> CreateAsync(CostCenterModel entity)
    {
        var code = AccountingEntityRules.NormalizeCode(entity.Code);
        AccountingEntityRules.ValidateCode(code);
        var parentCode = AccountingEntityRules.NormalizeOptionalCode(entity.ParentCode);

        if (await db.Context.CostCenters.AnyAsync(x => x.Code.ToUpper() == code))
        {
            throw new DefaultException($"Centro de custo {code} já cadastrado.");
        }

        await ValidateAsync(code, entity.Class, parentCode, entity.ValidFrom, entity.ValidTo);

        var costCenter = new CostCenter()
        {
            Code = code,
            Name = entity.Name,
            Inactive = entity.Inactive,
            Class = entity.Class,
            ParentCode = parentCode,
            ValidFrom = entity.ValidFrom,
            ValidTo = entity.ValidTo,
        };

        await db.Context.CostCenters.AddAsync(costCenter);
        await db.SaveChangesAsync();

        entity.Code = code;
        entity.ParentCode = parentCode;

        return entity;
    }

    public async Task<CostCenterModel?> UpdateAsync(string code, CostCenterModel entity)
    {
        var costCenter = await db.Context.CostCenters.FirstOrDefaultAsync(x => x.Code == code);

        if (costCenter == null)
        {
            return null;
        }

        var parentCode = AccountingEntityRules.NormalizeOptionalCode(entity.ParentCode);
        await ValidateAsync(costCenter.Code, entity.Class, parentCode, entity.ValidFrom, entity.ValidTo);

        costCenter.Name = entity.Name;
        costCenter.Inactive = entity.Inactive;
        costCenter.Class = entity.Class;
        costCenter.ParentCode = parentCode;
        costCenter.ValidFrom = entity.ValidFrom;
        costCenter.ValidTo = entity.ValidTo;

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

        return new CostCenterModel()
        {
            Code = costCenter.Code,
            Name = costCenter.Name,
            Inactive = costCenter.Inactive,
            Class = costCenter.Class,
            ParentCode = costCenter.ParentCode,
            ValidFrom = costCenter.ValidFrom,
            ValidTo = costCenter.ValidTo,
        };
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var costCenter = await db.Context.CostCenters.FirstOrDefaultAsync(x => x.Code == code);

        if (costCenter == null)
        {
            return false;
        }

        await AccountingEntityRules.EnsureNoChildrenAsync(db.Context.CostCenters, costCenter.Code, x => x.ParentCode);

        db.Context.CostCenters.Remove(costCenter);
        await db.SaveChangesAsync();

        return true;
    }

    private async Task ValidateAsync(
        string code, AccountingEntityClass cls, string? parentCode, DateTime? validFrom, DateTime? validTo)
    {
        AccountingEntityRules.ValidateValidity(validFrom, validTo);
        await AccountingEntityRules.ValidateAsync(
            db.Context.CostCenters, code, cls, parentCode, x => x.ParentCode, x => x.Class, x => x.Code);
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<CostCenterModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    private bool EntityExists(string code)
    {
        return db.Context.CostCenters.Any(x => x.Code == code);
    }
}
