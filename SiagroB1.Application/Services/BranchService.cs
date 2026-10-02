using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class BranchService(AppDbContext context, IConfiguration configuration) : IBranchService
{
    public async Task<Branch> CreateAsync(Branch entity)
    {
        var existingBranch = await context.Branchs
            .FirstOrDefaultAsync(b => b.Code == entity.Code);
        
        if (existingBranch != null)
        {
            throw new DefaultException($"Branch {entity.Code} already exists.");
        }

        ValidateNfeIssuance(entity);
        await ValidateIssuerFieldsAsync(entity);

        await context.Branchs.AddAsync(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var entity = await context.Branchs.FindAsync(key);
        if (entity == null)
        {
            return false;
        }

        context.Branchs.Remove(entity);
        await context.SaveChangesAsync();
        return true;
    }

    public IQueryable<Branch> QueryAll()
    {
        return context.Branchs.AsNoTracking();
    }

    public async Task<IEnumerable<Branch>> GetAllAsync()
    {
        return await context.Branchs.ToListAsync();
    }

    public async Task<Branch?> GetByIdAsync(string key)
    {
        return await context.Branchs.FindAsync(key);
    }

    public async Task<Branch?> UpdateAsync(string key, Branch entity)
    {
        ValidateNfeIssuance(entity);
        await ValidateIssuerFieldsAsync(entity);

        context.Entry(entity).State = EntityState.Modified;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(key))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                throw new DefaultException("Error updating entity.");
            }
        }

        return entity;
    }
    
    /// <summary>
    /// Com a chave ligada, o cálculo de tributos depende do CRT e da UF da filial. Validado
    /// enquanto a chave está ligada (não só na virada), para ninguém apagar o CRT depois.
    /// Só em STANDALONE: em SAPB1 a chave nem aparece na tela.
    /// </summary>
    private void ValidateNfeIssuance(Branch entity)
    {
        if (!entity.IssuesNfe || !ErpMode.IsStandalone(configuration))
            return;

        if (entity.TaxRegime is null || string.IsNullOrWhiteSpace(entity.StateCode))
            throw new DefaultException(
                "Para emitir NF-e pelo Siagro, informe o regime tributário e a UF da filial.");
    }

    /// <summary>
    /// Coerência dos dados do emitente (só STANDALONE): o município dá o cUF/cMun do XML e tem de
    /// ser da UF da filial; o CEP vai sem máscara. Nada aqui é obrigatório — quem exige o cadastro
    /// completo é a prontidão da emissão.
    /// </summary>
    private async Task ValidateIssuerFieldsAsync(Branch entity)
    {
        if (!ErpMode.IsStandalone(configuration))
            return;

        if (!string.IsNullOrWhiteSpace(entity.ZipCode) && !System.Text.RegularExpressions.Regex.IsMatch(entity.ZipCode, "^[0-9]{8}$"))
            throw new DefaultException("O CEP da filial deve ter 8 dígitos, sem traço.");

        if (string.IsNullOrWhiteSpace(entity.MunicipalityCode))
            return;

        var municipality = await context.Municipalities.AsNoTracking()
                               .FirstOrDefaultAsync(m => m.Code == entity.MunicipalityCode)
                           ?? throw new DefaultException($"Município {entity.MunicipalityCode} não encontrado.");

        if (!string.Equals(municipality.StateAbbreviation, entity.StateCode, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException(
                $"O município {municipality.Name} é de {municipality.StateAbbreviation}, mas a UF da filial é {entity.StateCode}.");
    }

    private bool EntityExists(string key)
    {
        return context.Branchs.Any(e => e.Code == key);
    }
}
