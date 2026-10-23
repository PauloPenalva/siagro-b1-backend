using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Ecf;

/// <summary>Ajustes do LALUR/LACS: CRUD com validação (período conforme a forma de apuração da filial).</summary>
public class EcfAdjustmentService(AppDbContext context, ILogger<EcfAdjustmentService> logger)
    : BaseService<EcfAdjustment, Guid>(context, logger)
{
    public override async Task<EcfAdjustment> CreateAsync(EcfAdjustment entity)
    {
        await ValidateAsync(entity);
        return await base.CreateAsync(entity);
    }

    public override async Task<EcfAdjustment?> UpdateAsync(Guid key, EcfAdjustment entity)
    {
        entity.Id = key;
        var stored = await context.Set<EcfAdjustment>().AsNoTracking().Where(a => a.Id == key)
            .Select(a => a.BranchCode).FirstOrDefaultAsync();
        if (stored is null) throw new KeyNotFoundException("Entity not found.");
        if (!string.Equals(stored, entity.BranchCode?.Trim(), StringComparison.Ordinal))
            throw new BusinessException("A filial não pode ser alterada.");
        await ValidateAsync(entity);
        return await base.UpdateAsync(key, entity);
    }

    private async Task ValidateAsync(EcfAdjustment a)
    {
        a.BranchCode = a.BranchCode?.Trim() ?? string.Empty;
        if (a.BranchCode.Length == 0) throw new BusinessException("Informe a filial.");
        await EcfRules.EnsureBranchAsync(_context, a.BranchCode);
        if (a.Year < 2000 || a.Year > 2100) throw new BusinessException("Ano do ajuste inválido.");
        if (!Enum.IsDefined(a.Tax)) throw new BusinessException("Tributo do ajuste inválido.");
        if (!Enum.IsDefined(a.Kind)) throw new BusinessException("Tipo do ajuste inválido.");

        a.TableCode = a.TableCode?.Trim() ?? string.Empty;
        if (a.TableCode.Length == 0) throw new BusinessException("Informe o código da tabela do ajuste.");
        EcfRules.MaxLength(a.TableCode, 20, "Código da tabela do ajuste");
        a.Description = a.Description?.Trim() ?? string.Empty;
        EcfRules.MaxLength(a.Description, 250, "Descrição do ajuste");
        if (a.Amount <= 0) throw new BusinessException("Valor do ajuste deve ser maior que zero.");

        var form = await _context.Set<EcfSettings>().AsNoTracking()
            .Where(s => s.BranchCode == a.BranchCode)
            .Select(s => (EcfAssessmentForm?)s.AssessmentForm)
            .FirstOrDefaultAsync();
        var valid = form switch
        {
            EcfAssessmentForm.Annual => a.Period == 0,
            EcfAssessmentForm.Quarterly => a.Period is >= 1 and <= 4,
            _ => a.Period is >= 0 and <= 4, // sem parâmetros ainda: aceita qualquer período válido
        };
        if (!valid) throw new BusinessException("Período inválido para a forma de apuração.");
    }
}
