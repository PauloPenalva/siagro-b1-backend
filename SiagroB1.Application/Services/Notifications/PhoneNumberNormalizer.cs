using System.Text;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Converte o telefone digitado no cadastro para o formato que o provedor de WhatsApp espera:
/// DDI + DDD + número, só dígitos (ex.: <c>5566999998888</c>).
///
/// Puro e estático: é a regra com mais casos de borda da feature e precisa ser testável sem
/// banco nem HTTP.
/// </summary>
public static class PhoneNumberNormalizer
{
    private const string BrazilCountryCode = "55";

    /// <summary>
    /// Devolve o número normalizado, ou <c>null</c> se não der para afirmar qual é o número.
    ///
    /// Nulo é intencional em vez de "melhor esforço": um número adivinhado errado não dá erro
    /// nenhum — a mensagem só não chega, e ninguém descobre. É melhor barrar no cadastro.
    /// </summary>
    public static string? ToE164Br(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = OnlyDigits(raw);

        return digits.Length switch
        {
            // DDD + fixo(8) ou DDD + celular(9): falta só o país.
            // Inclui o caso do DDD 55 (Rio Grande do Sul), que aqui recebe o DDI e vira 13
            // dígitos — tratar "começa com 55" como já-normalizado quebraria justamente ele.
            10 or 11 => BrazilCountryCode + digits,

            // Já tem o país: 55 + DDD + fixo(8) = 12, 55 + DDD + celular(9) = 13.
            12 or 13 when digits.StartsWith(BrazilCountryCode) => digits,

            _ => null,
        };
    }

    private static string OnlyDigits(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsAsciiDigit(character))
                builder.Append(character);
        }

        return builder.ToString();
    }
}
