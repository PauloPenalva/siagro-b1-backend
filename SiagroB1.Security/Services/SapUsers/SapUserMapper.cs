using System.Globalization;
using System.Text;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Entities.SAP;

namespace SiagroB1.Security.Services.SapUsers;

/// <summary>
/// Regras de espelhamento de uma linha do OUSR sobre um usuário do SiagroB1.
///
/// Fica isolada aqui porque os dois caminhos que sincronizam — o provisionamento pontual, no
/// login, e a varredura periódica — precisam aplicar exatamente as mesmas regras. Duas cópias
/// divergindo produziriam cadastros diferentes conforme o usuário tivesse logado ou não.
///
/// O que NUNCA é tocado: <c>PasswordHash</c>, <c>IsAdmin</c>, tema, foto e perfis. Senha e
/// permissões são do SiagroB1; do SAP vêm apenas identificação e situação.
/// </summary>
public static class SapUserMapper
{
    /// <summary>USERS.FullName é VARCHAR(100); OUSR.U_NAME vai até 155.</summary>
    public const int FullNameMaxLength = 100;

    public const int EmailMaxLength = 100;

    public static string Username(SapUser source) => source.UserCode.Trim();

    /// <summary>
    /// Chave de comparação equivalente à do banco.
    ///
    /// USERS.Username e USERS.Email têm índice único com collation
    /// <c>Latin1_General_100_CI_AI</c> - que ignora maiúsculas E acentos. Comparar em C# com
    /// <c>OrdinalIgnoreCase</c> trata "João" e "Joao" como nomes diferentes, o SQL Server trata
    /// como o mesmo, e a gravação estoura por chave duplicada. O OUSR do cliente tem exatamente
    /// esse par.
    /// </summary>
    public static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>Travado no SAP (<c>Locked = 'Y'</c>) equivale a inativo no SiagroB1.</summary>
    public static bool IsActive(SapUser source) =>
        !string.Equals(source.Locked?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);

    public static string FullName(SapUser source)
    {
        var name = source.UserName?.Trim();

        // Sem nome no SAP, o código do usuário é melhor do que uma tela mostrando vazio.
        if (string.IsNullOrEmpty(name))
        {
            name = Username(source);
        }

        return name.Length <= FullNameMaxLength ? name : name[..FullNameMaxLength];
    }

    /// <summary>
    /// E-mail a gravar, ou <c>null</c>.
    ///
    /// USERS.Email tem índice único: um e-mail em branco no SAP, longo demais, ou já usado por
    /// outro usuário viraria uma violação de índice que derrubaria a sincronização inteira — por
    /// isso vira <c>null</c> e o caso é registrado no log.
    /// </summary>
    public static string? Email(SapUser source, Func<string, bool> isTakenByAnotherUser)
    {
        var email = source.Email?.Trim();

        if (string.IsNullOrEmpty(email) || email.Length > EmailMaxLength || isTakenByAnotherUser(email))
        {
            return null;
        }

        return email;
    }

    /// <summary>
    /// Aplica a linha do OUSR sobre o usuário.
    /// </summary>
    /// <returns><c>true</c> se algum campo mudou - evita gravação e log desnecessários.</returns>
    public static bool Apply(SapUser source, User target, Func<string, bool> isEmailTakenByAnotherUser)
    {
        var fullName = FullName(source);
        var email = Email(source, isEmailTakenByAnotherUser);
        var isActive = IsActive(source);

        var changed = target.FullName != fullName || target.Email != email || target.IsActive != isActive;

        target.FullName = fullName;
        target.Email = email;
        target.IsActive = isActive;

        return changed;
    }

    /// <summary>Cria o usuário local a partir do SAP. Nasce sem senha: o primeiro acesso é pelo "esqueci minha senha".</summary>
    public static User CreateFrom(SapUser source, Func<string, bool> isEmailTakenByAnotherUser)
    {
        var user = new User
        {
            Username = Username(source),
            FullName = FullName(source),
            PasswordHash = null,
            CreatedAt = DateTime.Now
        };

        Apply(source, user, isEmailTakenByAnotherUser);
        return user;
    }
}
