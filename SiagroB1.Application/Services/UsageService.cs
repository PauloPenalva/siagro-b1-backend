using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>
/// Naturezas de operação do modo STANDALONE: identidade fiscal em USAGES, efeito de negócio
/// em USAGE_EFFECTS. O LEFT JOIN é o mesmo do modo SAPB1 — muda só a fonte da identidade.
/// </summary>
public class UsageService(IUnitOfWork db, ILogger<UsageService> logger)
    : IUsage
{
    public IQueryable<UsageModel> QueryAll()
    {
        return db.Context.Usages
            .GroupJoin(
                db.Context.UsageEffects,
                usage => usage.Code,
                effect => effect.UsageCode,
                (usage, effects) => new { usage, effects })
            .SelectMany(
                x => x.effects.DefaultIfEmpty(),
                (x, effect) => new UsageModel()
                {
                    Code = x.usage.Code,
                    Name = x.usage.Name,
                    Description = x.usage.Description,
                    CfopOutgoingInState = x.usage.CfopOutgoingInState,
                    CfopOutgoingOutState = x.usage.CfopOutgoingOutState,
                    CfopIncomingInState = x.usage.CfopIncomingInState,
                    CfopIncomingOutState = x.usage.CfopIncomingOutState,
                    Inactive = x.usage.Inactive,
                    Direction = x.usage.Direction,
                    InvoiceOperationText = x.usage.InvoiceOperationText,
                    DefaultAdditionalInfo = x.usage.DefaultAdditionalInfo,
                    MovesFiscalInventory = x.usage.MovesFiscalInventory,
                    CreatesFinancialDocument = x.usage.CreatesFinancialDocument,
                    IcmsInStateCst = x.usage.IcmsInStateCst,
                    IcmsInStateCsosn = x.usage.IcmsInStateCsosn,
                    IcmsInStateRate = x.usage.IcmsInStateRate,
                    IcmsInStateBaseReduction = x.usage.IcmsInStateBaseReduction,
                    IcmsInStateDeferral = x.usage.IcmsInStateDeferral,
                    IcmsInStateBenefitCode = x.usage.IcmsInStateBenefitCode,
                    IcmsOutStateCst = x.usage.IcmsOutStateCst,
                    IcmsOutStateCsosn = x.usage.IcmsOutStateCsosn,
                    IcmsOutStateBaseReduction = x.usage.IcmsOutStateBaseReduction,
                    IcmsOutStateDeferral = x.usage.IcmsOutStateDeferral,
                    IcmsOutStateBenefitCode = x.usage.IcmsOutStateBenefitCode,
                    PisCst = x.usage.PisCst,
                    PisRate = x.usage.PisRate,
                    CofinsCst = x.usage.CofinsCst,
                    CofinsRate = x.usage.CofinsRate,
                    ExcludeIcmsFromPisCofinsBase = x.usage.ExcludeIcmsFromPisCofinsBase,
                    IbsCbsCst = x.usage.IbsCbsCst,
                    IbsCbsClassCode = x.usage.IbsCbsClassCode,
                    IbsRateReduction = x.usage.IbsRateReduction,
                    CbsRateReduction = x.usage.CbsRateReduction,
                    ReturnUsageCode = x.usage.ReturnUsageCode,
                    ReturnUsageName = x.usage.ReturnUsage != null ? x.usage.ReturnUsage.Name : null,
                    ContractBalanceEffect = effect != null ? effect.ContractBalanceEffect : 0,
                    ContractValueEffect = effect != null ? effect.ContractValueEffect : 0,
                    RequiresContract = effect != null && effect.RequiresContract,
                    RequiresQuantity = effect == null || effect.RequiresQuantity,
                    RequiresWeight = effect != null && effect.RequiresWeight,
                    IsDefault = effect != null && effect.IsDefault,
                    HasConfiguredEffects = effect != null,
                })
            .AsNoTracking();
    }

    public async Task<UsageModel?> GetByIdAsync(int key)
    {
        try
        {
            return await QueryAll().FirstOrDefaultAsync(x => x.Code == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<IEnumerable<UsageModel>> GetAllAsync()
    {
        return await QueryAll().ToListAsync();
    }

    public async Task<UsageModel> CreateAsync(UsageModel entity)
    {
        UsageEffectWriter.ValidateEffects(entity);
        ValidateDirectionRules(entity);
        UsageTaxationValidator.Validate(entity);
        await ValidateReturnUsageAsync(entity, key: null);

        var usage = new Usage { Name = entity.Name };
        UsageTaxationMapper.CopyToEntity(entity, usage);

        await db.Context.Usages.AddAsync(usage);

        // Precisa do Code gerado antes de gravar o efeito, que é chaveado por ele.
        await db.SaveChangesAsync();

        await UsageEffectWriter.WriteAsync(db, usage.Code, entity);
        await db.SaveChangesAsync();

        entity.Code = usage.Code;
        entity.HasConfiguredEffects = true;

        return entity;
    }

    public async Task<UsageModel?> UpdateAsync(int key, UsageModel entity)
    {
        UsageEffectWriter.ValidateEffects(entity);
        ValidateDirectionRules(entity);
        UsageTaxationValidator.Validate(entity);
        await ValidateReturnUsageAsync(entity, key);

        var usage = await db.Context.Usages.FirstOrDefaultAsync(x => x.Code == key);

        if (usage == null)
        {
            return null;
        }

        var newDirection = entity.Direction ?? UsageDirection.Outgoing;

        // Linha de documento já gravada com esta natureza ficaria apontando para uma natureza de
        // outro tipo — o CFOP congelado nela deixaria de corresponder ao cadastro.
        if (newDirection != usage.Direction &&
            await db.Context.SalesInvoicesItems.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} já foi utilizada em documento de saída. " +
                "O tipo não pode ser alterado.");
        }

        // Natureza de entrada que é a devolução de outra natureza viraria uma "devolução" de saída:
        // a devolução criada pelo Devolver passaria a nascer com CFOP de saída.
        if (newDirection == UsageDirection.Outgoing && usage.Direction == UsageDirection.Incoming &&
            await db.Context.Usages.AnyAsync(x => x.ReturnUsageCode == key))
        {
            throw new DefaultException(
                $"A natureza {usage.Name} é a natureza de devolução de outra natureza de saída: " +
                "o tipo não pode virar Saída.");
        }

        UsageTaxationMapper.CopyToEntity(entity, usage);

        await UsageEffectWriter.WriteAsync(db, key, entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }

            throw new DefaultException("Error updating entity.");
        }

        entity.Code = usage.Code;
        entity.HasConfiguredEffects = true;

        return entity;
    }

    public async Task<bool> DeleteAsync(int key)
    {
        var usage = await db.Context.Usages.FirstOrDefaultAsync(x => x.Code == key);

        if (usage == null)
        {
            return false;
        }

        // Não há FK para USAGES (é cadastro dual-mode), então a integridade referencial
        // mora aqui: o banco não recusaria a exclusão de uma natureza já usada.
        // A natureza é de LINHA, então quem a referencia é o item, não o cabeçalho.
        if (await db.Context.SalesInvoicesItems.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} já foi utilizada em documento de saída. " +
                "Inative-a em vez de excluir.");
        }

        // Com a FK de USAGES.ReturnUsageCode o banco recusaria com um 547 sem mensagem de negócio.
        if (await db.Context.Usages.AnyAsync(x => x.ReturnUsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} é a natureza de devolução de outra natureza. " +
                "Inative-a em vez de excluir.");
        }

        var effect = await db.Context.UsageEffects.FirstOrDefaultAsync(x => x.UsageCode == key);

        if (effect != null)
        {
            db.Context.UsageEffects.Remove(effect);
        }

        db.Context.Usages.Remove(usage);
        await db.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Regras ligadas ao tipo. A natureza padrão é a do faturamento de romaneio, que é SAÍDA:
    /// uma de entrada ali faria o documento de saída nascer com CFOP de entrada.
    /// </summary>
    private static void ValidateDirectionRules(UsageModel model)
    {
        if (model.Direction == UsageDirection.Incoming && model.IsDefault)
        {
            throw new DefaultException(
                "Natureza de operação de entrada não pode ser a padrão do faturamento de romaneio.");
        }
    }

    /// <summary>Natureza de devolução (spec §5): só em natureza de Saída e apontando uma de Entrada ativa.</summary>
    private async Task ValidateReturnUsageAsync(UsageModel model, int? key)
    {
        if (model.ReturnUsageCode is not { } returnCode)
            return;

        if (model.Direction == UsageDirection.Incoming)
            throw new DefaultException("Só natureza de saída tem natureza de devolução.");

        if (key == returnCode)
            throw new DefaultException("A natureza de devolução não pode ser a própria natureza.");

        var target = await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(x => x.Code == returnCode)
                     ?? throw new DefaultException($"Natureza de devolução {returnCode} não encontrada.");

        if (target.Direction != UsageDirection.Incoming)
            throw new DefaultException($"A natureza de devolução {target.Name} precisa ser de entrada.");

        if (target.Inactive)
            throw new DefaultException($"A natureza de devolução {target.Name} está inativa.");
    }

    private bool EntityExists(int key)
    {
        return db.Context.Usages.Any(x => x.Code == key);
    }
}
