using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Certificado aberto + configuração da filial, para uma chamada. Descarta o certificado no fim.</summary>
public sealed class NfeServiceContext(BranchNfeSettings branchSettings, NfeServiceSettings settings) : IDisposable
{
    public BranchNfeSettings BranchSettings { get; } = branchSettings;

    public NfeServiceSettings Settings { get; } = settings;

    public void Dispose() => Settings.Certificate.Dispose();
}

/// <summary>Configuração da NF-e por filial (spec §7). Só STANDALONE: recusa nos demais modos.</summary>
public class BranchNfeSettingsService(IUnitOfWork db, NfeOptions options, INfeSefazClient sefaz)
{
    private const string StandaloneOnly = "A configuração da NF-e só existe no modo STANDALONE.";

    public async Task<BranchNfeSettingsModel> GetAsync(string branchCode)
    {
        EnsureStandalone();

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().Include(s => s.SeriesList).FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is null)
        {
            settings = new BranchNfeSettings { BranchCode = branchCode };
            settings.SeriesList.Add(DefaultSeriesRow(branchCode));
        }

        return await ToModelAsync(settings);
    }

    public async Task<BranchNfeSettingsModel> SaveAsync(
        string branchCode, NfeEnvironment environment, int defaultSeries,
        IReadOnlyList<BranchNfeSeriesInput> series, string userName)
    {
        EnsureStandalone();

        if (!Enum.IsDefined(environment))
            throw new DefaultException("Escolha o ambiente: produção ou homologação.");

        if (series.Count == 0)
            throw new DefaultException("Cadastre ao menos uma série.");

        foreach (var item in series)
        {
            if (item.Series is < 0 or > 999)
                throw new DefaultException("A série da NF-e vai de 0 a 999.");

            if (item.NextNumber is < 1 or > 999_999_999)
                throw new DefaultException("O próximo número da NF-e vai de 1 a 999.999.999.");

            var description = item.Description?.Trim() ?? string.Empty;

            if (description.Length == 0)
                throw new DefaultException($"Informe a descrição da série {item.Series}.");

            if (description.Length > 60)
                throw new DefaultException($"A descrição da série {item.Series} vai até 60 caracteres.");
        }

        var duplicate = series.GroupBy(x => x.Series).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new DefaultException($"A série {duplicate.Key} aparece mais de uma vez.");

        if (series.All(x => x.Series != defaultSeries))
            throw new DefaultException("Escolha a série padrão entre as séries cadastradas.");

        var settings = await LoadOrCreateAsync(branchCode);

        // Voltar a numeração com o mesmo ambiente reemitiria números já usados (rejeição 539).
        // Trocar o ambiente começa outra numeração e libera qualquer número; série nova também.
        if (settings.Environment == environment)
        {
            foreach (var item in series)
            {
                var existing = settings.SeriesList.FirstOrDefault(x => x.Series == item.Series);

                if (existing is not null && item.NextNumber < existing.NextNumber)
                    throw new DefaultException(
                        $"O próximo número da série {item.Series} não pode voltar: a numeração já usada geraria " +
                        "rejeição 539. Para recomeçar, troque o ambiente ou cadastre outra série.");
            }
        }

        var removed = settings.SeriesList.Where(x => series.All(i => i.Series != x.Series)).ToList();

        if (removed.Count > 0)
        {
            var used = await UsedSeriesAsync(branchCode);
            var blocked = removed.Where(x => used.Contains(x.Series)).OrderBy(x => x.Series).FirstOrDefault();

            if (blocked is not null)
                throw new DefaultException($"A série {blocked.Series} já foi usada em NF-e e não pode ser excluída.");
        }

        settings.Environment = environment;
        settings.DefaultSeries = defaultSeries;

        foreach (var row in removed)
        {
            settings.SeriesList.Remove(row);
            db.Context.BranchNfeSeries.Remove(row);
        }

        foreach (var item in series)
        {
            var row = settings.SeriesList.FirstOrDefault(x => x.Series == item.Series);

            if (row is null)
                settings.SeriesList.Add(new BranchNfeSeries
                {
                    BranchCode = branchCode, Series = item.Series, Description = item.Description.Trim(), NextNumber = item.NextNumber,
                });
            else
            {
                row.Description = item.Description.Trim();
                row.NextNumber = item.NextNumber;
            }
        }

        Stamp(settings, userName);

        await db.SaveChangesAsync();

        return await ToModelAsync(settings);
    }

    public async Task<BranchNfeSettingsModel> UploadCertificateAsync(
        string branchCode, byte[] pfx, string password, string userName)
    {
        EnsureStandalone();

        // Primeiro a chave do servidor: sem ela nada do que vem depois serve.
        var cipher = options.Cipher();

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");

        var info = CertificateInspector.Inspect(pfx, password);

        if (!info.HasPrivateKey)
            throw new DefaultException("O certificado não tem a chave privada: exporte o .pfx com a chave.");

        if (info.ValidUntil < DateTime.Now)
            throw new DefaultException($"O certificado venceu em {info.ValidUntil:dd/MM/yyyy}.");

        if (info.TaxId is null)
            throw new DefaultException("Não foi possível ler o CNPJ do certificado.");

        // A raiz (8 primeiros caracteres) basta: o certificado da matriz assina pelas filiais.
        var branchTaxId = NfeText.AlphaNumeric(branch.TaxId);
        if (branchTaxId.Length < 8 || !info.TaxId.StartsWith(branchTaxId[..8], StringComparison.Ordinal))
            throw new DefaultException(
                $"O certificado é do CNPJ {info.TaxId}, que não é da raiz do CNPJ da filial ({branch.TaxId}).");

        var settings = await LoadOrCreateAsync(branchCode);
        settings.CertificatePfx = pfx;
        settings.CertificatePasswordCipher = cipher.Encrypt(password);
        settings.CertificateSubject = info.Subject.Length <= 250 ? info.Subject : info.Subject[..250];
        settings.CertificateTaxId = info.TaxId;
        settings.CertificateValidUntil = info.ValidUntil;
        Stamp(settings, userName);

        await db.SaveChangesAsync();

        return await ToModelAsync(settings);
    }

    public async Task<NfeServiceStatusDto> TestConnectionAsync(string branchCode)
    {
        using var context = await OpenAsync(branchCode);

        var result = await sefaz.ServiceStatusAsync(context.Settings);

        return new NfeServiceStatusDto { StatusCode = result.StatusCode, Reason = result.Reason };
    }

    /// <summary>
    /// Abre o certificado da filial para uma chamada à SEFAZ. <paramref name="environment"/> força
    /// o ambiente — a consulta usa o da emissão, mesmo que a configuração tenha mudado depois.
    /// </summary>
    public async Task<NfeServiceContext> OpenAsync(string branchCode, NfeEnvironment? environment = null)
    {
        EnsureStandalone();

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().Include(s => s.SeriesList).FirstOrDefaultAsync(s => s.BranchCode == branchCode)
                       ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração da NF-e.");

        if (settings.CertificatePfx is null || settings.CertificatePasswordCipher is null)
            throw new DefaultException("Envie o certificado digital da filial na Configuração da NF-e.");

        var branch = await db.Context.Branchs.AsNoTracking().FirstAsync(b => b.Code == branchCode);

        // Valida a UF antes de abrir o certificado: o que abre e não é entregue ninguém descarta.
        if (string.IsNullOrWhiteSpace(branch.StateCode))
            throw new DefaultException($"A filial {branchCode} está sem UF.");

        var password = options.Cipher().Decrypt(settings.CertificatePasswordCipher);
        var certificate = CertificateLoader.Load(settings.CertificatePfx, password);

        return new NfeServiceContext(settings, new NfeServiceSettings(
            environment ?? settings.Environment,
            branch.StateCode,
            certificate,
            NfeServiceSettings.DefaultSchemasDirectory,
            options.TimeoutMilliseconds,
            options.ValidateSefazCertificate));
    }

    private void EnsureStandalone()
    {
        if (!options.IsStandalone)
            throw new DefaultException(StandaloneOnly);
    }

    private async Task<BranchNfeSettings> LoadOrCreateAsync(string branchCode)
    {
        var settings = await db.Context.BranchNfeSettings.Include(s => s.SeriesList).FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is not null)
            return settings;

        if (!await db.Context.Branchs.AnyAsync(b => b.Code == branchCode))
            throw new DefaultException($"Filial {branchCode} não encontrada.");

        settings = new BranchNfeSettings { BranchCode = branchCode };
        // Toda configuração nasce com a série padrão: a emissão sempre encontra o contador.
        settings.SeriesList.Add(DefaultSeriesRow(branchCode));
        db.Context.BranchNfeSettings.Add(settings);

        return settings;
    }

    private static void Stamp(BranchNfeSettings settings, string userName)
    {
        settings.UpdatedAt = DateTime.Now;
        settings.UpdatedBy = userName;
    }

    private static BranchNfeSeries DefaultSeriesRow(string branchCode) =>
        new() { BranchCode = branchCode, Series = 1, Description = "Padrão", NextNumber = 1 };

    /// <summary>
    /// Séries já vistas em NF-e própria da filial (documento com ambiente preenchido): qualquer
    /// saída, ou entrada de emissão própria. Série de nota de terceiro não conta.
    /// </summary>
    private async Task<HashSet<int>> UsedSeriesAsync(string branchCode)
    {
        var sales = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.BranchCode == branchCode && i.NfeEnvironment != null)
            .Select(i => i.TaxDocumentSeries).Distinct().ToListAsync();

        var purchases = await db.Context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.BranchCode == branchCode && i.NfeEnvironment != null && i.IssuerType == DocumentIssuerType.Own)
            .Select(i => i.TaxDocumentSeries).Distinct().ToListAsync();

        return sales.Concat(purchases)
            .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? (int?)n : null)
            .Where(n => n is not null).Select(n => n!.Value).ToHashSet();
    }

    private async Task<BranchNfeSettingsModel> ToModelAsync(BranchNfeSettings settings)
    {
        var used = await UsedSeriesAsync(settings.BranchCode);

        return new BranchNfeSettingsModel
        {
            BranchCode = settings.BranchCode,
            Environment = settings.Environment,
            DefaultSeries = settings.DefaultSeries,
            SeriesList = settings.SeriesList.OrderBy(x => x.Series).Select(x => new BranchNfeSeriesModel
            {
                Series = x.Series, Description = x.Description, NextNumber = x.NextNumber, IsUsed = used.Contains(x.Series),
            }).ToList(),
            HasCertificate = settings.CertificatePfx is not null,
            CertificateSubject = settings.CertificateSubject,
            CertificateTaxId = settings.CertificateTaxId,
            CertificateValidUntil = settings.CertificateValidUntil,
            UpdatedAt = settings.UpdatedAt,
            UpdatedBy = settings.UpdatedBy,
            ServerKeyConfigured = options.HasCertificateKey,
        };
    }
}
