using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Cadastro das dimensões gerenciais (tabela local ACCOUNTING_DIMENSIONS, só STANDALONE;
/// registrado apenas em AddStandAloneServices).
/// </summary>
public class AccountingDimensionService(IUnitOfWork db) : IAccountingDimensionService
{
    private static readonly Dictionary<DimensionRole, string> RoleNames = new()
    {
        [DimensionRole.Vehicle] = "Veículo",
        [DimensionRole.Partner] = "Parceiro",
    };

    public IQueryable<AccountingDimension> QueryAll()
    {
        return db.Context.AccountingDimensions.AsNoTracking();
    }

    public async Task<AccountingDimension?> UpdateAsync(int number, AccountingDimension changes)
    {
        var dimension = await db.Context.AccountingDimensions.FirstOrDefaultAsync(x => x.Number == number);

        if (dimension == null)
        {
            return null;
        }

        var name = (changes.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            throw new DefaultException("Informe o nome da dimensão.");
        }

        if (number == 1 && !changes.Active)
        {
            throw new DefaultException("A dimensão 1 é o centro de custo e não pode ser desativada.");
        }

        if (changes.Role != DimensionRole.None)
        {
            var other = await db.Context.AccountingDimensions
                .FirstOrDefaultAsync(x => x.Number != number && x.Role == changes.Role);

            if (other != null)
            {
                var roleName = RoleNames.GetValueOrDefault(changes.Role, changes.Role.ToString());
                throw new DefaultException($"A dimensão {other.Name} já tem o papel {roleName}.");
            }
        }

        dimension.Name = name;
        dimension.Active = changes.Active;
        dimension.Role = changes.Role;

        await db.SaveChangesAsync();

        return dimension;
    }
}
