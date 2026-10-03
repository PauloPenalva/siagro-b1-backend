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

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        return ToModel(settings ?? new BranchNfeSettings { BranchCode = branchCode });
    }

    public async Task<BranchNfeSettingsModel> SaveAsync(
        string branchCode, NfeEnvironment environment, int series, int nextNumber, string userName)
    {
        EnsureStandalone();

        if (!Enum.IsDefined(environment))
            throw new DefaultException("Escolha o ambiente: produção ou homologação.");

        if (series is < 0 or > 999)
            throw new DefaultException("A série da NF-e vai de 0 a 999.");

        if (nextNumber is < 1 or > 999_999_999)
            throw new DefaultException("O próximo número da NF-e vai de 1 a 999.999.999.");

        var settings = await LoadOrCreateAsync(branchCode);

        // Voltar a numeração com o mesmo ambiente e série reemitiria números já usados (rejeição
        // 539). Trocar a série ou o ambiente começa outra numeração e libera qualquer número.
        if (settings.Environment == environment && settings.Series == series && nextNumber < settings.NextNumber)
            throw new DefaultException(
                "O próximo número não pode voltar: a numeração já usada geraria rejeição 539. " +
                "Para recomeçar, troque a série ou o ambiente.");

        settings.Environment = environment;
        settings.Series = series;
        settings.NextNumber = nextNumber;
        Stamp(settings, userName);

        await db.SaveChangesAsync();

        return ToModel(settings);
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

        return ToModel(settings);
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

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode)
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
        var settings = await db.Context.BranchNfeSettings.FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is not null)
            return settings;

        if (!await db.Context.Branchs.AnyAsync(b => b.Code == branchCode))
            throw new DefaultException($"Filial {branchCode} não encontrada.");

        settings = new BranchNfeSettings { BranchCode = branchCode };
        db.Context.BranchNfeSettings.Add(settings);

        return settings;
    }

    private static void Stamp(BranchNfeSettings settings, string userName)
    {
        settings.UpdatedAt = DateTime.Now;
        settings.UpdatedBy = userName;
    }

    private BranchNfeSettingsModel ToModel(BranchNfeSettings settings) => new()
    {
        BranchCode = settings.BranchCode,
        Environment = settings.Environment,
        Series = settings.Series,
        NextNumber = settings.NextNumber,
        HasCertificate = settings.CertificatePfx is not null,
        CertificateSubject = settings.CertificateSubject,
        CertificateTaxId = settings.CertificateTaxId,
        CertificateValidUntil = settings.CertificateValidUntil,
        UpdatedAt = settings.UpdatedAt,
        UpdatedBy = settings.UpdatedBy,
        ServerKeyConfigured = options.HasCertificateKey,
    };
}
