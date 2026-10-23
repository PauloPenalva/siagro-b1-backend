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
    public override Task<EcfPartner> CreateAsync(EcfPartner entity)
    {
        Validate(entity);
        return base.CreateAsync(entity);
    }

    public override Task<EcfPartner?> UpdateAsync(Guid key, EcfPartner entity)
    {
        Validate(entity);
        return base.UpdateAsync(key, entity);
    }

    private static void Validate(EcfPartner p)
    {
        p.BranchCode = p.BranchCode?.Trim() ?? string.Empty;
        if (p.BranchCode.Length == 0) throw new BusinessException("Informe a filial.");
        p.Name = p.Name?.Trim() ?? string.Empty;
        if (p.Name.Length == 0) throw new BusinessException("Informe o nome do sócio.");

        var taxId = TaxIdValidator.Normalize(p.TaxId);
        if ((taxId.Length != 11 && taxId.Length != 14) || !TaxIdValidator.IsValid(taxId))
            throw new BusinessException("CPF/CNPJ do sócio inválido.");
        p.TaxId = taxId;

        p.Qualification = p.Qualification?.Trim() ?? string.Empty;
        if (!EcfRules.IsTwoDigits(p.Qualification)) throw new BusinessException("Qualificação do sócio deve ter 2 dígitos.");

        if (p.VotingCapitalPercent is < 0 or > 100 || p.TotalCapitalPercent is < 0 or > 100)
            throw new BusinessException("Percentual deve estar entre 0 e 100.");

        p.CountryCode = string.IsNullOrWhiteSpace(p.CountryCode) ? "105" : p.CountryCode.Trim();
    }
}
