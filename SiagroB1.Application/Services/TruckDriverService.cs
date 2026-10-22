using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

/// <summary>
/// O código do motorista é o CPF (o CT-e e o MDF-e o leem dali). Só a inclusão valida: há códigos
/// antigos fora do padrão que precisam continuar editáveis. Só STANDALONE: em SAPB1 (sem CT-e/MDF-e)
/// o código continua livre.
/// </summary>
public class TruckDriverService(AppDbContext context, IConfiguration configuration, ILogger<TruckDriverService> logger) :
    BaseService<TruckDriver, string>(context, logger), ITruckDriverService
{
    private static readonly HashSet<string> LicenseCategories = ["A", "B", "C", "D", "E", "AB", "AC", "AD", "AE"];

    public override async Task<TruckDriver> CreateAsync(TruckDriver entity)
    {
        if (!ErpMode.IsStandalone(configuration))
        {
            ValidateLicense(entity);
            return await base.CreateAsync(entity);
        }

        var cpf = TaxIdValidator.Normalize(entity.Code);

        if (cpf.Length != 11 || !TaxIdValidator.IsValid(cpf))
            throw new DefaultException("Informe um CPF válido no código do motorista.");

        if (await _context.Set<TruckDriver>().AnyAsync(x => x.Code == cpf))
            throw new DefaultException($"Motorista com CPF {cpf} já cadastrado.");

        entity.Code = cpf;
        ValidateLicense(entity);

        return await base.CreateAsync(entity);
    }

    public override async Task<TruckDriver?> UpdateAsync(string key, TruckDriver entity)
    {
        ValidateLicense(entity);

        return await base.UpdateAsync(key, entity);
    }

    private static void ValidateLicense(TruckDriver driver)
    {
        driver.LicenseCategory = string.IsNullOrWhiteSpace(driver.LicenseCategory) ? null : driver.LicenseCategory.Trim();

        if (driver.LicenseCategory is not null && !LicenseCategories.Contains(driver.LicenseCategory))
            throw new DefaultException("Categoria da CNH inválida.");
    }
}
