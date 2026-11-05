namespace SiagroB1.Web.Market;

/// <summary>
/// Chave string de entity set como chega à action. Dentro do <c>$batch</c> o valor da rota vem ainda
/// codificado ("DOL%20COM") e, conforme o binder, entre aspas ("'DOL%20COM'"); direto, já vem limpo.
/// Mesma regra de <c>BusinessPartnersAddressesController.Unquote</c>.
/// </summary>
public static class ODataKeyNormalizer
{
    public static string Normalize(string key)
    {
        var value = Uri.UnescapeDataString(key);

        // Uma aspa de cada ponta, não Trim: código que termina em apóstrofo a perderia.
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            value = value[1..^1];

        return value.Replace("''", "'");
    }
}
