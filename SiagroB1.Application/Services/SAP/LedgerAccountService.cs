using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Contas contábeis lidas de OACT. O cadastro é mantido no SAP: as operações de
/// escrita não são suportadas neste modo.
/// </summary>
public class LedgerAccountService(SapErpDbContext context, ILogger<LedgerAccountService> logger)
    : ILedgerAccountService
{
    public IQueryable<LedgerAccountModel> QueryAll()
    {
        return context.LedgerAccounts
            .Where(x => x.Postable == "Y" && x.FrozenFor != "Y")
            .Select(x => new LedgerAccountModel()
            {
                Code = x.Code,
                Name = x.Name,
                // O SAP não classifica a conta em Ativo/Passivo/Receita/Despesa
                // nos moldes deste cadastro: o campo fica vazio em modo SAPB1.
                Type = null,
                AllowsPosting = true,
                Inactive = false,
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

    public Task<IEnumerable<LedgerAccountModel>> GetAllAsync()
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<LedgerAccountModel> CreateAsync(LedgerAccountModel entity)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<LedgerAccountModel?> UpdateAsync(string code, LedgerAccountModel entity)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<bool> DeleteAsync(string code)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<LedgerAccountModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }
}
