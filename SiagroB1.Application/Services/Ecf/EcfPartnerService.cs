using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Ecf;

/// <summary>Quadro societário da ECF (Y600): CRUD com validação.</summary>
public class EcfPartnerService(AppDbContext context, ILogger<EcfPartnerService> logger)
    : BaseService<EcfPartner, Guid>(context, logger)
{
    public override async Task<EcfPartner> CreateAsync(EcfPartner entity)
    {
        await ValidateAsync(entity);
        return await base.CreateAsync(entity);
    }

    public override async Task<EcfPartner?> UpdateAsync(Guid key, EcfPartner entity)
    {
        // O Id da rota manda; o do corpo é ignorado.
        entity.Id = key;
        var stored = await context.EcfPartners.AsNoTracking().Where(p => p.Id == key)
            .Select(p => p.BranchCode).FirstOrDefaultAsync();
        if (stored is null) throw new KeyNotFoundException("Entity not found.");
        if (!string.Equals(stored, entity.BranchCode?.Trim(), StringComparison.Ordinal))
            throw new BusinessException("A filial não pode ser alterada.");
        await ValidateAsync(entity);
        return await base.UpdateAsync(key, entity);
    }

    private async Task ValidateAsync(EcfPartner p)
    {
        p.BranchCode = p.BranchCode?.Trim() ?? string.Empty;
        if (p.BranchCode.Length == 0) throw new BusinessException("Informe a filial.");
        await EcfRules.EnsureBranchAsync(context, p.BranchCode);
        p.Name = p.Name?.Trim() ?? string.Empty;
        if (p.Name.Length == 0) throw new BusinessException("Informe o nome do sócio.");
        EcfRules.MaxLength(p.Name, 100, "Nome do sócio");

        var taxId = TaxIdValidator.Normalize(p.TaxId);
        if ((taxId.Length != 11 && taxId.Length != 14) || !TaxIdValidator.IsValid(taxId))
            throw new BusinessException("CPF/CNPJ do sócio inválido.");
        p.TaxId = taxId;

        p.Qualification = p.Qualification?.Trim() ?? string.Empty;
        if (!EcfRules.IsTwoDigits(p.Qualification)) throw new BusinessException("Qualificação do sócio deve ter 2 dígitos.");

        if (p.VotingCapitalPercent is < 0 or > 100 || p.TotalCapitalPercent is < 0 or > 100)
            throw new BusinessException("Percentual deve estar entre 0 e 100.");

        p.CountryCode = string.IsNullOrWhiteSpace(p.CountryCode) ? "105" : p.CountryCode.Trim();
        if (p.CountryCode.Length != 3 || !p.CountryCode.All(char.IsAsciiDigit))
            throw new BusinessException("Código do país deve ter 3 dígitos.");
    }
}
