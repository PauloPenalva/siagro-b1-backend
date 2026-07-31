using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Dtos;
using SiagroB1.Security.Shared;

namespace SiagroB1.Security.Services;

/// <summary>
/// Manutenção que o próprio usuário faz na sua conta: foto do avatar, tema e senha.
///
/// Tudo aqui é escopado ao usuário da sessão — nenhum método recebe o alvo por parâmetro vindo da
/// requisição, justamente para que um usuário não consiga alterar o perfil de outro.
/// </summary>
public class UserProfileService(
    CommonDbContext db,
    PasswordPolicy passwordPolicy,
    ILogger<UserProfileService> logger)
{
    /// <summary>Temas aceitos. Um valor fora da lista quebraria o carregamento da UI.</summary>
    public static readonly string[] AllowedThemes =
        ["sap_fiori_3", "sap_fiori_3_dark", "sap_horizon", "sap_horizon_dark"];

    private const int MaxPhotoBytes = 2 * 1024 * 1024;

    private static readonly string[] AllowedPhotoContentTypes =
        ["image/png", "image/jpeg", "image/gif", "image/webp"];

    public async Task<UserProfileDto?> GetProfileAsync(string username, CancellationToken ct = default)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
        {
            return null;
        }

        return new UserProfileDto
        {
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            Theme = user.Theme,
            // O blob nunca vai junto: quem precisa da imagem busca no endpoint da foto.
            HasPhoto = user.PhotoContent != null && user.PhotoContent.Length > 0,
            PasswordRequirements = passwordPolicy.Description
        };
    }

    public async Task<(byte[] Content, string ContentType)?> GetPhotoAsync(
        string username, CancellationToken ct = default)
    {
        var photo = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == username)
            .Select(u => new { u.PhotoContent, u.PhotoContentType })
            .FirstOrDefaultAsync(ct);

        if (photo?.PhotoContent is null || photo.PhotoContent.Length == 0)
        {
            return null;
        }

        return (photo.PhotoContent, photo.PhotoContentType ?? "image/png");
    }

    public async Task<OperationResult> SetPhotoAsync(
        string username, string? contentType, string? base64Content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(base64Content))
        {
            return OperationResult.Fail("Nenhuma imagem foi enviada.");
        }

        if (string.IsNullOrWhiteSpace(contentType) ||
            !AllowedPhotoContentTypes.Contains(contentType.Trim().ToLowerInvariant()))
        {
            return OperationResult.Fail("Formato de imagem não suportado. Use PNG, JPEG, GIF ou WEBP.");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(base64Content);
        }
        catch (FormatException)
        {
            return OperationResult.Fail("Imagem inválida.");
        }

        if (content.Length == 0)
        {
            return OperationResult.Fail("Imagem inválida.");
        }

        if (content.Length > MaxPhotoBytes)
        {
            return OperationResult.Fail($"A imagem deve ter no máximo {MaxPhotoBytes / (1024 * 1024)} MB.");
        }

        var user = await FindAsync(username, ct);
        if (user is null)
        {
            return OperationResult.Fail("Usuário não encontrado.");
        }

        user.PhotoContent = content;
        user.PhotoContentType = contentType.Trim().ToLowerInvariant();
        await db.SaveChangesAsync(ct);

        return OperationResult.Ok("Foto atualizada.");
    }

    public async Task<OperationResult> RemovePhotoAsync(string username, CancellationToken ct = default)
    {
        var user = await FindAsync(username, ct);
        if (user is null)
        {
            return OperationResult.Fail("Usuário não encontrado.");
        }

        user.PhotoContent = null;
        user.PhotoContentType = null;
        await db.SaveChangesAsync(ct);

        return OperationResult.Ok("Foto removida.");
    }

    public async Task<OperationResult> SetThemeAsync(
        string username, string? theme, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(theme) || !AllowedThemes.Contains(theme.Trim()))
        {
            return OperationResult.Fail("Tema inválido.");
        }

        var user = await FindAsync(username, ct);
        if (user is null)
        {
            return OperationResult.Fail("Usuário não encontrado.");
        }

        user.Theme = theme.Trim();
        await db.SaveChangesAsync(ct);

        return OperationResult.Ok("Tema atualizado.");
    }

    /// <summary>
    /// Troca da própria senha, exigindo a senha atual — sem isso, um computador deixado
    /// desbloqueado permitiria a qualquer um assumir a conta em definitivo.
    /// </summary>
    public async Task<OperationResult> ChangePasswordAsync(
        string username, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(username, ct);
        if (user is null)
        {
            return OperationResult.Fail("Usuário não encontrado.");
        }

        if (!PasswordHasher.Verify(user.PasswordHash, currentPassword, out _))
        {
            return OperationResult.Fail("Senha atual incorreta.");
        }

        if (!passwordPolicy.IsValid(newPassword, out var error))
        {
            return OperationResult.Fail(error);
        }

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Senha alterada pelo próprio usuário: {Username}", username);

        return OperationResult.Ok("Senha alterada com sucesso.");
    }

    private Task<User?> FindAsync(string username, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
}
