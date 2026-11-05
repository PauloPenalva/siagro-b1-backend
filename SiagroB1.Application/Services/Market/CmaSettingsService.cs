using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Cma;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

public sealed record CmaSettingsInput(bool Enabled, string? Host, string? UserName, string? Password);

public sealed record CmaSettingsView(bool Enabled, string Host, string? UserName, bool HasPassword);

/// <summary>Conexão com a CMA da empresa (linha única). A senha é cifrada com a chave Nfe:CertificateKey.</summary>
public class CmaSettingsService(AppDbContext context, NfeOptions nfeOptions)
{
    private const int SingleKey = 1;

    public async Task<CmaSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.CmaSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SingleKey, cancellationToken)
            ?? new CmaSettings { Key = SingleKey };
        return ToView(row);
    }

    public async Task<CmaSettingsView> SaveAsync(CmaSettingsInput input, CancellationToken cancellationToken = default)
    {
        var password = string.IsNullOrEmpty(input.Password) ? null : input.Password;
        if (password is not null && !nfeOptions.HasCertificateKey)
            throw new BusinessException("Chave de criptografia (Nfe:CertificateKey) não configurada");

        var row = await context.CmaSettings.FirstOrDefaultAsync(x => x.Key == SingleKey, cancellationToken);
        if (row is null)
        {
            row = new CmaSettings { Key = SingleKey };
            context.CmaSettings.Add(row);
        }

        row.Enabled = input.Enabled;
        if (!string.IsNullOrWhiteSpace(input.Host))
            row.Host = input.Host.Trim();
        row.UserName = string.IsNullOrWhiteSpace(input.UserName) ? null : input.UserName.Trim();
        // Senha vazia mantém a cifra anterior.
        if (password is not null)
            row.PasswordEncrypted = nfeOptions.Cipher().Encrypt(password);

        await context.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    /// <summary>Credenciais abertas para o conector; nulo se desligado ou sem usuário/senha.</summary>
    public async Task<CmaCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.CmaSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SingleKey, cancellationToken);
        if (row is not { Enabled: true, PasswordEncrypted.Length: > 0 } || string.IsNullOrWhiteSpace(row.UserName))
            return null;

        return new CmaCredentials(row.Host, row.UserName, nfeOptions.Cipher().Decrypt(row.PasswordEncrypted));
    }

    private static CmaSettingsView ToView(CmaSettings row) =>
        new(row.Enabled, row.Host, row.UserName, row.PasswordEncrypted is { Length: > 0 });
}
