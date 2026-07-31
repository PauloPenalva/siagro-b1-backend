using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Interfaces;
using SiagroB1.Security.Shared;

namespace SiagroB1.Security.Services;

/// <summary>
/// Recuperação de senha por e-mail.
///
/// Três invariantes moldam esta classe:
/// <list type="bullet">
/// <item>o token em claro só existe no e-mail — o banco guarda apenas o hash;</item>
/// <item>o token é de uso único e expira em 30 minutos;</item>
/// <item>redefinir a senha derruba todas as sessões abertas do usuário, porque quem pede a
/// recuperação normalmente é quem perdeu o controle da conta.</item>
/// </list>
///
/// <see cref="RequestAsync"/> nunca informa se o usuário existe: o endpoint é público e a
/// diferença de comportamento o transformaria num verificador de contas válidas.
/// </summary>
public class PasswordResetService(
    CommonDbContext db,
    IEmailSender emailSender,
    ISapUserProvisioner sapUserProvisioner,
    PasswordPolicy passwordPolicy,
    IConfiguration configuration,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    /// <summary>Regra vigente, para a tela avisar exatamente o que o servidor vai cobrar.</summary>
    public string PasswordRequirements => passwordPolicy.Description;

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Prefixo único em TODAS as saídas do pedido de recuperação.
    ///
    /// A resposta ao usuário é sempre a mesma - "enviaremos um link" - exista ou não a conta, para
    /// o endpoint público não virar verificador de contas válidas. O efeito colateral é que
    /// desistir por usuário inexistente, por falta de e-mail ou por excesso de pedidos fica
    /// indistinguível de um envio bem-sucedido. Só o log conta a verdade, e por isso ele precisa
    /// de um termo único para procurar.
    /// </summary>
    private const string LogPrefix = "RECUPERACAO-SENHA";

    /// <summary>
    /// Janela e teto do throttle: evita que o endpoint público vire uma metralhadora de e-mails.
    /// Configurável porque o padrão (3 por 15 min) é apertado para quem está testando a tela.
    /// </summary>
    private TimeSpan ThrottleWindow =>
        TimeSpan.FromMinutes(
            Math.Max(configuration.GetValue("Security:PasswordReset:ThrottleWindowMinutes", 15), 1));

    private int MaxRequestsPerWindow =>
        Math.Max(configuration.GetValue("Security:PasswordReset:MaxRequestsPerWindow", 3), 1);

    public async Task RequestAsync(string usernameOrEmail, string? requestIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
        {
            logger.LogWarning("{Prefix}: pedido sem identificador. Nada enviado.", LogPrefix);
            return;
        }

        var identifier = usernameOrEmail.Trim();

        // Em modo SAPB1 o usuário pode existir só no OUSR e nunca ter logado: sem isto, quem
        // acabou de ser cadastrado no SAP não conseguiria definir a primeira senha.
        await sapUserProvisioner.EnsureAsync(identifier, ct);

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.IsActive && (u.Username == identifier || u.Email == identifier), ct);

        // Comparação por e-mail sem diferenciar maiúsculas: o usuário digita como quiser.
        user ??= await db.Users.FirstOrDefaultAsync(
            u => u.IsActive && u.Email != null && u.Email.ToUpper() == identifier.ToUpper(), ct);

        // Os três desfechos abaixo são silenciosos para o usuário e idênticos entre si na tela.
        // Todos saem no log em Warning e com o mesmo prefixo, para que uma única busca responda
        // "por que não chegou?".
        if (user is null)
        {
            logger.LogWarning(
                "{Prefix}: nenhum usuário ATIVO encontrado para '{Identificador}'. Nada enviado.",
                LogPrefix, identifier);
            return;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning(
                "{Prefix}: usuário {Username} não tem e-mail cadastrado. Nada enviado.",
                LogPrefix, user.Username);
            return;
        }

        var maxRequests = MaxRequestsPerWindow;
        var window = ThrottleWindow;
        var since = DateTime.Now - window;
        var recentRequests = await db.PasswordResetTokens
            .CountAsync(t => t.UserId == user.Id && t.CreatedAt >= since, ct);

        if (recentRequests >= maxRequests)
        {
            logger.LogWarning(
                "{Prefix}: usuário {Username} excedeu o limite de {Max} pedidos em {Janela} min. " +
                "Nada enviado - aguarde ou ajuste Security:PasswordReset:MaxRequestsPerWindow.",
                LogPrefix, user.Username, maxRequests, window.TotalMinutes);
            return;
        }

        var token = GenerateToken();

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(token),
            CreatedAt = DateTime.Now,
            ExpiresAt = DateTime.Now.Add(TokenLifetime),
            RequestIp = requestIp
        });

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "{Prefix}: link gerado para {Username} ({Email}). Veja a mensagem logo abaixo.",
            LogPrefix, user.Username, user.Email);

        await emailSender.SendAsync(user.Email, "Redefinição de senha - SIAGRO B1", BuildEmailBody(user, token), ct);
    }

    public async Task<bool> ValidateAsync(string token, CancellationToken ct = default) =>
        await FindUsableTokenAsync(token, ct) is not null;

    public async Task<PasswordResetResult> ResetAsync(
        string token, string newPassword, CancellationToken ct = default)
    {
        var resetToken = await FindUsableTokenAsync(token, ct);

        if (resetToken is null)
        {
            return new PasswordResetResult(false,
                "Link de redefinição inválido ou expirado. Solicite um novo.");
        }

        // Validado depois de achar o token, mas ANTES de consumi-lo: uma senha fraca não pode
        // queimar o link, senão o usuário perde a tentativa por um erro de digitação.
        if (!passwordPolicy.IsValid(newPassword, out var error))
        {
            return new PasswordResetResult(false, error);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == resetToken.UserId, ct);

        if (user is null || !user.IsActive)
        {
            return new PasswordResetResult(false,
                "Link de redefinição inválido ou expirado. Solicite um novo.");
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);

        var now = DateTime.Now;

        // Uso único, e todos os outros pedidos pendentes do mesmo usuário caem junto.
        foreach (var pending in await db.PasswordResetTokens
                     .Where(t => t.UserId == user.Id && t.UsedAt == null)
                     .ToListAsync(ct))
        {
            pending.UsedAt = now;
        }

        // Quem trocou a senha por ter perdido o controle da conta não pode continuar com o
        // invasor logado numa sessão antiga.
        foreach (var session in await db.UserSessions
                     .Where(s => s.UserId == user.Id && s.IsActive)
                     .ToListAsync(ct))
        {
            session.IsActive = false;
            session.ExpiresAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Senha redefinida por link de recuperação: {Username}", user.Username);

        return new PasswordResetResult(true, "Senha redefinida com sucesso.");
    }

    private async Task<PasswordResetToken?> FindUsableTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = HashToken(token);

        return await db.PasswordResetTokens.FirstOrDefaultAsync(
            t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > DateTime.Now, ct);
    }

    /// <summary>32 bytes de aleatoriedade criptográfica, em base64url para caber numa query string.</summary>
    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string BuildEmailBody(User user, string token)
    {
        var baseUrl = (configuration["Security:AppBaseUrl"] ?? "").TrimEnd('/');

        // "/index.html" explícito, e não apenas "/": o servidor de desenvolvimento do UI5 responde
        // a raiz com a listagem do diretório, e o link chegaria quebrado em quem testa localmente.
        // O Gateway serve o mesmo arquivo nos dois caminhos, então a URL vale em produção também.
        var link = $"{baseUrl}/index.html#/reset-password?token={Uri.EscapeDataString(token)}";

        return $"""
                <p>Olá, {System.Net.WebUtility.HtmlEncode(user.FullName)}.</p>
                <p>Recebemos um pedido para redefinir a senha do usuário <b>{System.Net.WebUtility.HtmlEncode(user.Username)}</b> no SIAGRO B1.</p>
                <p><a href="{link}">Clique aqui para cadastrar uma nova senha</a></p>
                <p>Ou copie e cole este endereço no navegador:<br>{link}</p>
                <p>O link vale por {TokenLifetime.TotalMinutes:0} minutos e só pode ser usado uma vez.</p>
                <p>Se você não pediu esta redefinição, ignore este e-mail: sua senha atual continua valendo.</p>
                """;
    }
}
