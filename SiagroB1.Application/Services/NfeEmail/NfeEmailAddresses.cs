using System.Text.RegularExpressions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.NfeEmail;

/// <summary>Regras de e-mail de NF-e compartilhadas: cadastro do parceiro e reenvio manual.</summary>
public static class NfeEmailAddresses
{
    public const int Max = 10;

    /// <summary>Apara, descarta vazios e valida formato, duplicidade (sem diferenciar caixa) e o máximo de 10.</summary>
    public static List<string> Normalize(IEnumerable<string?>? emails)
    {
        var list = (emails ?? [])
            .Select(e => (e ?? "").Trim())
            .Where(e => e.Length > 0)
            .ToList();

        foreach (var email in list)
        {
            if (email.Length > 250 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                throw new DefaultException($"E-mail de NF-e inválido: {email}");
        }

        if (list.Count > Max)
            throw new DefaultException($"Informe no máximo {Max} e-mails de NF-e.");

        var duplicate = list.GroupBy(e => e, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new DefaultException($"E-mail de NF-e repetido: {duplicate.Key}");

        return list;
    }
}
