using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>Configuração do MDF-e por filial (só STANDALONE): liga a emissão, ambiente, tipo de carga padrão, CIOT e séries.</summary>
public class BranchMdfeSettingsService(IUnitOfWork db, IConfiguration configuration)
{
    public const string StandaloneOnly = "O MDF-e só existe no modo STANDALONE.";
    public const string EnableRequirements = "Para emitir MDF-e, cadastre o certificado (Configuração da NF-e) e o RNTRC (Filial → Transportadora).";

    public async Task<BranchMdfeSettingsModel> GetAsync(string branchCode)
    {
        EnsureStandalone();

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().Include(s => s.SeriesList)
            .FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is null)
        {
            settings = new BranchMdfeSettings { BranchCode = branchCode };
            settings.SeriesList.Add(DefaultSeriesRow(branchCode));
        }

        return ToModel(settings, await UsedSeriesAsync(branchCode));
    }

    public async Task<BranchMdfeSettingsModel> SaveAsync(string branchCode, BranchMdfeSettingsModel model, string userName)
    {
        EnsureStandalone();

        if (!Enum.IsDefined(model.Environment))
            throw new DefaultException("Escolha o ambiente: produção ou homologação.");

        var cargoType = model.DefaultCargoType?.Trim() ?? string.Empty;
        if (!(cargoType.Length == 2 && int.TryParse(cargoType, out var cargoCode) && cargoCode is >= 1 and <= 13))
            throw new DefaultException("Escolha o tipo de carga padrão (01 a 13).");

        var series = model.SeriesList;
        if (series.Count == 0)
            throw new DefaultException("Cadastre ao menos uma série.");

        foreach (var item in series)
        {
            if (item.Series is < 0 or > 999)
                throw new DefaultException("A série do MDF-e vai de 0 a 999.");

            if (item.NextNumber is < 1 or > 999_999_999)
                throw new DefaultException("O próximo número do MDF-e vai de 1 a 999.999.999.");

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
        var usedSeries = await UsedSeriesAsync(branchCode);
        var blocked = removed.Where(x => usedSeries.Contains(x.Series)).OrderBy(x => x.Series).FirstOrDefault();
        if (blocked is not null)
            throw new DefaultException($"A série {blocked.Series} já foi usada em MDF-e e não pode ser excluída.");

        settings.Enabled = model.Enabled;
        settings.Environment = model.Environment;
        settings.DefaultSeries = model.DefaultSeries;
        settings.DefaultCargoType = cargoType;
        settings.RequiresCiot = model.RequiresCiot;

        foreach (var row in removed)
        {
            settings.SeriesList.Remove(row);
            db.Context.BranchMdfeSeries.Remove(row);
        }

        foreach (var item in series)
        {
            var row = settings.SeriesList.FirstOrDefault(x => x.Series == item.Series);

            if (row is null)
                settings.SeriesList.Add(new BranchMdfeSeries
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

        return ToModel(settings, await UsedSeriesAsync(branchCode));
    }

    private void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StandaloneOnly);
    }

    private async Task<BranchMdfeSettings> LoadOrCreateAsync(string branchCode)
    {
        var settings = await db.Context.BranchMdfeSettings.Include(s => s.SeriesList).FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is not null)
            return settings;

        if (!await db.Context.Branchs.AnyAsync(b => b.Code == branchCode))
            throw new DefaultException($"Filial {branchCode} não encontrada.");

        settings = new BranchMdfeSettings { BranchCode = branchCode };
        settings.SeriesList.Add(DefaultSeriesRow(branchCode));
        db.Context.BranchMdfeSettings.Add(settings);

        return settings;
    }

    private static BranchMdfeSeries DefaultSeriesRow(string branchCode) =>
        new() { BranchCode = branchCode, Series = 1, Description = "Padrão", NextNumber = 1 };

    // Série usada = o contador já andou (próximo número acima de 1). Ainda não há tabela de MDF-e emitidos;
    // quando houver, troque por "existe MDF-e dela com número" como no CT-e.
    private async Task<HashSet<int>> UsedSeriesAsync(string branchCode) =>
        (await db.Context.BranchMdfeSeries.AsNoTracking()
            .Where(x => x.BranchCode == branchCode && x.NextNumber > 1)
            .Select(x => x.Series).ToListAsync()).ToHashSet();

    private static BranchMdfeSettingsModel ToModel(BranchMdfeSettings settings, HashSet<int> usedSeries) => new()
    {
        BranchCode = settings.BranchCode,
        Enabled = settings.Enabled,
        Environment = settings.Environment,
        DefaultSeries = settings.DefaultSeries,
        DefaultCargoType = settings.DefaultCargoType,
        RequiresCiot = settings.RequiresCiot,
        SeriesList = settings.SeriesList.OrderBy(x => x.Series).Select(x => new BranchMdfeSeriesModel
        {
            Series = x.Series, Description = x.Description, NextNumber = x.NextNumber, IsUsed = usedSeries.Contains(x.Series),
        }).ToList(),
        UpdatedAt = settings.UpdatedAt,
        UpdatedBy = settings.UpdatedBy,
    };
}
