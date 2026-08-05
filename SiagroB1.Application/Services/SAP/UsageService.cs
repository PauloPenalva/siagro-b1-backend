using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Naturezas de operação do modo SAPB1: identidade fiscal lida de <c>OUSG</c> (cadastro
/// mantido no SAP) combinada com o efeito de negócio de <c>USAGE_EFFECTS</c>, que é do
/// Siagro.
///
/// É essa divisão que faz o efeito funcionar em SAPB1. Enquanto efeito e identidade moravam
/// na mesma linha, o modo não tinha onde gravá-lo — as naturezas chegavam todas sem efeito e
/// nenhum documento mexia no contrato.
///
/// Criar e excluir continuam recusados: a identidade é do SAP. <see cref="UpdateAsync"/>
/// aceita a alteração, mas grava SOMENTE o efeito.
/// </summary>
public class UsageService(
    SapErpDbContext context,
    IUnitOfWork db,
    ILogger<UsageService> logger)
    : IUsage
{
    public IQueryable<UsageModel> QueryAll()
    {
        // Duas fontes em bancos DIFERENTES (OUSG no SAP, USAGE_EFFECTS no Siagro): não dá
        // para o SQL Server juntá-las num único plano, então o join é em memória. O volume é
        // de cadastro (dezenas de linhas), não de movimento.
        var effects = db.Context.UsageEffects.AsNoTracking().ToList();

        return context.Usages
            .AsNoTracking()
            .ToList()
            .GroupJoin(
                effects,
                usage => usage.Code,
                effect => effect.UsageCode,
                (usage, matches) => new { usage, effect = matches.FirstOrDefault() })
            .Select(x => new UsageModel()
            {
                Code = x.usage.Code,
                Name = x.usage.Name,
                Description = x.usage.Description,
                CfopIncomingInState = x.usage.CfopIncomingInState,
                CfopIncomingOutState = x.usage.CfopIncomingOutState,
                CfopIncomingImport = x.usage.CfopIncomingImport,
                CfopOutgoingInState = x.usage.CfopOutgoingInState,
                CfopOutgoingOutState = x.usage.CfopOutgoingOutState,
                CfopOutgoingExport = x.usage.CfopOutgoingExport,
                Inactive = false,
                ContractBalanceEffect = x.effect?.ContractBalanceEffect ?? 0,
                ContractValueEffect = x.effect?.ContractValueEffect ?? 0,
                RequiresContract = x.effect?.RequiresContract ?? false,
                RequiresQuantity = x.effect?.RequiresQuantity ?? true,
                RequiresWeight = x.effect?.RequiresWeight ?? false,
                IsDefault = x.effect?.IsDefault ?? false,
                HasConfiguredEffects = x.effect != null,
            })
            .AsQueryable();
    }

    public Task<UsageModel?> GetByIdAsync(int key)
    {
        try
        {
            return Task.FromResult(QueryAll().FirstOrDefault(x => x.Code == key));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public Task<IEnumerable<UsageModel>> GetAllAsync()
    {
        return Task.FromResult(QueryAll().AsEnumerable());
    }

    public Task<UsageModel> CreateAsync(UsageModel model)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    /// <summary>
    /// Grava SOMENTE o efeito. Nome e CFOP vêm do SAP e são ignorados aqui — a tela os mostra
    /// em somente-leitura neste modo.
    /// </summary>
    public async Task<UsageModel?> UpdateAsync(int key, UsageModel model)
    {
        var current = await GetByIdAsync(key);

        if (current == null)
        {
            return null;
        }

        UsageEffectWriter.ValidateEffects(model);

        await UsageEffectWriter.WriteAsync(db, key, model);
        await db.SaveChangesAsync();

        return await GetByIdAsync(key);
    }

    public Task<bool> DeleteAsync(int key)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }
}
