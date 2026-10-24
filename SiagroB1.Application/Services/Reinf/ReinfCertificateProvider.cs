using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Reinf;

public interface IReinfCertificateProvider
{
    /// <summary>Certificado A1 que assina e transmite os eventos do contribuinte; descarte ao terminar.</summary>
    Task<X509Certificate2> GetAsync(string taxIdRoot, CancellationToken ct = default);
}

/// <summary>
/// Abre o certificado da filial matriz (CNPJ raiz + "0001") da Configuração da NF-e, como
/// <c>MdfeServiceContextFactory</c>, e confere que a raiz do CNPJ do certificado é a do contribuinte.
/// </summary>
public class ReinfCertificateProvider(AppDbContext context, NfeOptions options) : IReinfCertificateProvider
{
    public async Task<X509Certificate2> GetAsync(string taxIdRoot, CancellationToken ct = default)
    {
        var root = ReinfFormat.Root(taxIdRoot);
        var branches = await context.Branchs.AsNoTracking().ToListAsync(ct);
        var head = branches.FirstOrDefault(b => ReinfFormat.Digits(b.TaxId).StartsWith(root + "0001", StringComparison.Ordinal))
                   ?? throw new BusinessException($"Nenhuma filial matriz (CNPJ {root}/0001) cadastrada.");

        var settings = await context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == head.Code, ct);
        if (settings?.CertificatePfx is null || settings.CertificatePasswordCipher is null)
            throw new BusinessException($"Filial {head.Code} sem certificado A1 válido.");

        X509Certificate2 cert;
        try
        {
            cert = CertificateLoader.Load(settings.CertificatePfx, options.Cipher().Decrypt(settings.CertificatePasswordCipher));
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            throw new BusinessException($"Filial {head.Code} sem certificado A1 válido.");
        }

        var certRoot = ReinfFormat.Digits(CertificateInspector.Inspect(cert).TaxId);
        certRoot = certRoot.Length >= 8 ? certRoot[..8] : certRoot;
        if (certRoot != root)
        {
            cert.Dispose();
            throw new BusinessException($"Certificado com raiz {certRoot} diferente do contribuinte {root}.");
        }
        return cert;
    }
}
