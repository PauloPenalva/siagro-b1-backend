using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>Certificado aberto para uma chamada à SEFAZ do CT-e; descarte ao terminar.</summary>
public sealed class CteServiceContext(CteServiceSettings settings) : IDisposable
{
    public CteServiceSettings Settings { get; } = settings;

    public void Dispose() => Settings.Certificate.Dispose();
}

/// <summary>
/// Abre o certificado da filial (o da Configuração da NF-e) para o CT-e, espelhando
/// <see cref="BranchNfeSettingsService.OpenAsync"/>. O ambiente vem de quem chama: o do CT-e, nunca o da NF-e.
/// </summary>
public class CteServiceContextFactory(IUnitOfWork db, NfeOptions options)
{
    public virtual async Task<CteServiceContext> OpenAsync(string branchCode, NfeEnvironment environment)
    {
        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode)
                       ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração da NF-e (certificado).");

        if (settings.CertificatePfx is null || settings.CertificatePasswordCipher is null)
            throw new DefaultException("Envie o certificado digital da filial na Configuração da NF-e.");

        var state = await CteIssueInputAssembler.IssuerStateAsync(db, branchCode);

        var password = options.Cipher().Decrypt(settings.CertificatePasswordCipher);
        var certificate = CertificateLoader.Load(settings.CertificatePfx, password);

        return new CteServiceContext(new CteServiceSettings(
            environment, state, certificate, CteServiceSettings.DefaultSchemasDirectory, options.TimeoutMilliseconds));
    }
}
