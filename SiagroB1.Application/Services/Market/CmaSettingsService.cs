using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market.Cma;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market;

/// <summary>
/// <c>BlankPassword</c>: a conta da CMA não tem senha (caso real). É uma escolha explícita — campo de senha vazio
/// sem essa marca continua significando "mantenha a senha que já está gravada".
/// </summary>
public sealed record CmaSettingsInput(bool Enabled, string? Host, string? UserName, string? Password, bool BlankPassword = false);

public sealed record CmaSettingsView(bool Enabled, string Host, string? UserName, bool HasPassword, bool BlankPassword);

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
        // "Sem senha" grava uma senha vazia CIFRADA (não uma coluna vazia): o conector só liga com algo gravado,
        // então uma conta sem senha precisa ser dita, não deduzida de um campo esquecido em branco.
        var password = input.BlankPassword ? string.Empty : string.IsNullOrEmpty(input.Password) ? null : input.Password;
        if (password is not null && !nfeOptions.HasCertificateKey)
            throw new BusinessException("Chave de criptografia (Nfe:CertificateKey) não configurada");

        var row = await context.CmaSettings.FirstOrDefaultAsync(x => x.Key == SingleKey, cancellationToken);
        if (row is null)
        {
            row = new CmaSettings { Key = SingleKey };
            context.CmaSettings.Add(row);
        }

        // Host vazio mantém o anterior. Trocar o host exige a senha de novo: a senha guardada nunca vai a um host novo.
        if (!string.IsNullOrWhiteSpace(input.Host))
        {
            var host = input.Host.Trim();
            ValidateHost(host);
            if (!string.Equals(host, row.Host, StringComparison.OrdinalIgnoreCase)
                && row.PasswordEncrypted is { Length: > 0 } && password is null)
                throw new BusinessException("Informe a senha novamente ao alterar o host");
            row.Host = host;
        }

        row.Enabled = input.Enabled;
        row.UserName = string.IsNullOrWhiteSpace(input.UserName) ? null : input.UserName.Trim();
        // Senha vazia mantém a cifra anterior.
        if (password is not null)
            row.PasswordEncrypted = nfeOptions.Cipher().Encrypt(password);

        await context.SaveChangesAsync(cancellationToken);
        return ToView(row);
    }

    /// <summary>Credenciais abertas para o conector; nulo se desligado ou sem usuário/senha (senha vazia gravada conta).</summary>
    public async Task<CmaCredentials?> GetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.CmaSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SingleKey, cancellationToken);
        if (row is not { Enabled: true, PasswordEncrypted.Length: > 0 } || string.IsNullOrWhiteSpace(row.UserName))
            return null;

        return new CmaCredentials(row.Host, row.UserName, nfeOptions.Cipher().Decrypt(row.PasswordEncrypted));
    }

    private static void ValidateHost(string host)
    {
        if (!Uri.TryCreate(host, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || host.Contains('?') || host.Contains('#'))
            throw new BusinessException("Host da CMA inválido: informe só o endereço https:// (sem caminho, parâmetros ou fragmento).");
    }

    private CmaSettingsView ToView(CmaSettings row) =>
        new(row.Enabled, row.Host, row.UserName, row.PasswordEncrypted is { Length: > 0 }, IsBlankPassword(row));

    /// <summary>Só diz se a senha gravada é a vazia; a senha em si nunca sai daqui.</summary>
    private bool IsBlankPassword(CmaSettings row)
    {
        if (row.PasswordEncrypted is not { Length: > 0 } || !nfeOptions.HasCertificateKey)
            return false;
        try
        {
            return nfeOptions.Cipher().Decrypt(row.PasswordEncrypted).Length == 0;
        }
        catch (Exception)
        {
            // Chave trocada ou dado corrompido: a tela mostra "senha gravada" e o conector acusa o erro ao conectar.
            return false;
        }
    }
}
