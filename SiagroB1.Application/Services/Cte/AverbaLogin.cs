using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Application.Services.Cte;

/// <summary>Credenciais da AT&amp;M da filial: validação e login, compartilhados pela averbação e pelo cancelamento dela.</summary>
internal static class AverbaLogin
{
    public static bool IsConfigured(BranchTransportSettings? settings) =>
        settings is { AverbaEnabled: true, AverbaPasswordCipher.Length: > 0 }
        && !string.IsNullOrWhiteSpace(settings.AverbaUser) && !string.IsNullOrWhiteSpace(settings.AverbaCode);

    public static Task<AverbaLoginResult> LoginAsync(IAverbaClient averba, IConfiguration configuration, BranchTransportSettings settings, CancellationToken ct)
    {
        var password = CertificatePasswordCipher.FromBase64(configuration[CertificatePasswordCipher.ConfigurationKey]).Decrypt(settings.AverbaPasswordCipher!);
        return averba.LoginAsync(settings.AverbaEnvironment, settings.AverbaUser!.Trim(), password, settings.AverbaCode!.Trim(), ct);
    }
}
