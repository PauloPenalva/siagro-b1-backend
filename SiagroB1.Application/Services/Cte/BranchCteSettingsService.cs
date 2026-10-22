using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>Configuração do CT-e por filial (só STANDALONE): liga a emissão, ambiente, unidade padrão e séries.</summary>
public class BranchCteSettingsService(IUnitOfWork db, IConfiguration configuration)
{
    public const string StandaloneOnly = "O CT-e só existe no modo STANDALONE.";
    public const string EnableRequirements = "Para emitir CT-e, cadastre o certificado (Configuração da NF-e) e o RNTRC (Filial → Transportadora).";

    public async Task<BranchCteSettingsModel> GetAsync(string branchCode)
    {
        EnsureStandalone();

        var settings = await db.Context.BranchCteSettings.AsNoTracking().Include(s => s.SeriesList)
            .FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is null)
        {
            settings = new BranchCteSettings { BranchCode = branchCode };
            settings.SeriesList.Add(DefaultSeriesRow(branchCode));
        }

        return ToModel(settings);
    }

    public async Task<BranchCteSettingsModel> SaveAsync(string branchCode, BranchCteSettingsModel model, string userName)
    {
        EnsureStandalone();

        if (!Enum.IsDefined(model.Environment))
            throw new DefaultException("Escolha o ambiente: produção ou homologação.");

        var unit = model.DefaultQuantityUnit?.Trim() ?? string.Empty;
        if (unit is not ("00" or "01" or "02" or "03" or "04" or "05"))
            throw new DefaultException("Escolha a unidade padrão da quantidade: M3, KG, TON, UNIDADE, LITROS ou MMBTU.");

        var series = model.SeriesList;
        if (series.Count == 0)
            throw new DefaultException("Cadastre ao menos uma série.");

        foreach (var item in series)
        {
            if (item.Series is < 0 or > 999)
                throw new DefaultException("A série do CT-e vai de 0 a 999.");

            if (item.NextNumber is < 1 or > 999_999_999)
                throw new DefaultException("O próximo número do CT-e vai de 1 a 999.999.999.");

            var description = item.Description?.Trim() ?? string.Empty;

            if (description.Length == 0)
                throw new DefaultException($"Informe a descrição da série {item.Series}.");

            if (description.Length > 60)
                throw new DefaultException($"A descrição da série {item.Series} vai até 60 caracteres.");
        }

        var duplicate = series.GroupBy(x => x.Series).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new DefaultException($"A série {duplicate.Key} aparece mais de uma vez.");

        if (series.All(x => x.Series != model.DefaultSeries))
            throw new DefaultException("Escolha a série padrão entre as séries cadastradas.");

        if (model.Enabled)
        {
            var hasCertificate = await db.Context.BranchNfeSettings.AsNoTracking()
                .AnyAsync(x => x.BranchCode == branchCode && x.CertificatePfx != null);
            var hasRntrc = await db.Context.BranchTransportSettings.AsNoTracking()
                .AnyAsync(x => x.BranchCode == branchCode && x.CompanyRntrc != null && x.CompanyRntrc != "");

            if (!hasCertificate || !hasRntrc)
                throw new DefaultException(EnableRequirements);
        }

        var settings = await LoadOrCreateAsync(branchCode);

        // Voltar a numeração no mesmo ambiente reemitiria números já usados. Trocar o ambiente recomeça.
        if (settings.Environment == model.Environment)
        {
            foreach (var item in series)
            {
                var existing = settings.SeriesList.FirstOrDefault(x => x.Series == item.Series);

                if (existing is not null && item.NextNumber < existing.NextNumber)
                    throw new DefaultException(
                        $"O próximo número da série {item.Series} não pode voltar: a numeração já usada geraria " +
                        "rejeição por duplicidade. Para recomeçar, troque o ambiente ou cadastre outra série.");
            }
        }

        var removed = settings.SeriesList.Where(x => series.All(i => i.Series != x.Series)).ToList();
        var blocked = removed.Where(IsUsed).OrderBy(x => x.Series).FirstOrDefault();
        if (blocked is not null)
            throw new DefaultException($"A série {blocked.Series} já foi usada em CT-e e não pode ser excluída.");

        settings.Enabled = model.Enabled;
        settings.Environment = model.Environment;
        settings.DefaultSeries = model.DefaultSeries;
        settings.DefaultQuantityUnit = unit;

        foreach (var row in removed)
        {
            settings.SeriesList.Remove(row);
            db.Context.BranchCteSeries.Remove(row);
        }

        foreach (var item in series)
        {
            var row = settings.SeriesList.FirstOrDefault(x => x.Series == item.Series);

            if (row is null)
                settings.SeriesList.Add(new BranchCteSeries
                {
                    BranchCode = branchCode, Series = item.Series, Description = item.Description.Trim(), NextNumber = item.NextNumber,
                });
            else
            {
                row.Description = item.Description.Trim();
                row.NextNumber = item.NextNumber;
            }
        }

        settings.UpdatedAt = DateTime.Now;
        settings.UpdatedBy = userName;

        await db.SaveChangesAsync();

        return ToModel(settings);
    }

    private void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StandaloneOnly);
    }

    private async Task<BranchCteSettings> LoadOrCreateAsync(string branchCode)
    {
        var settings = await db.Context.BranchCteSettings.Include(s => s.SeriesList).FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is not null)
            return settings;

        if (!await db.Context.Branchs.AnyAsync(b => b.Code == branchCode))
            throw new DefaultException($"Filial {branchCode} não encontrada.");

        settings = new BranchCteSettings { BranchCode = branchCode };
        settings.SeriesList.Add(DefaultSeriesRow(branchCode));
        db.Context.BranchCteSettings.Add(settings);

        return settings;
    }

    private static BranchCteSeries DefaultSeriesRow(string branchCode) =>
        new() { BranchCode = branchCode, Series = 1, Description = "Padrão", NextNumber = 1 };

    // Ainda não há documento de CT-e: série usada = contador já andou.
    private static bool IsUsed(BranchCteSeries row) => row.NextNumber > 1;

    private static BranchCteSettingsModel ToModel(BranchCteSettings settings) => new()
    {
        BranchCode = settings.BranchCode,
        Enabled = settings.Enabled,
        Environment = settings.Environment,
        DefaultSeries = settings.DefaultSeries,
        DefaultQuantityUnit = settings.DefaultQuantityUnit,
        SeriesList = settings.SeriesList.OrderBy(x => x.Series).Select(x => new BranchCteSeriesModel
        {
            Series = x.Series, Description = x.Description, NextNumber = x.NextNumber, IsUsed = IsUsed(x),
        }).ToList(),
        UpdatedAt = settings.UpdatedAt,
        UpdatedBy = settings.UpdatedBy,
    };
}
