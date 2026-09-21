using System.Globalization;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>Valor e data por extenso em pt-BR para os placeholders <c>*_extenso</c>.</summary>
public static class ContractDraftNumberSpeller
{
    private static readonly string[] Units =
        ["zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
         "onze", "doze", "treze", "quatorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"];
    private static readonly string[] Tens =
        ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];
    private static readonly string[] Hundreds =
        ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos",
         "setecentos", "oitocentos", "novecentos"];

    public static string Currency(decimal value)
    {
        value = decimal.Round(value, 2, MidpointRounding.ToEven);
        var reais = (long)decimal.Truncate(value);
        var centavos = (int)((value - reais) * 100);

        var text = reais switch
        {
            0 => "zero real",
            1 => "um real",
            _ => Integer(reais) + (reais >= 1_000_000 && reais % 1_000_000 == 0 ? " de reais" : " reais"),
        };

        if (centavos > 0)
            text += $" e {Integer(centavos)} {(centavos == 1 ? "centavo" : "centavos")}";

        return text;
    }

    public static string Date(DateTime date) =>
        $"{date.Day} de {date.ToString("MMMM", CultureInfo.GetCultureInfo("pt-BR"))} de {date.Year}";

    private static string Integer(long n)
    {
        if (n < 20) return Units[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? " e " + Units[n % 10] : "");
        if (n == 100) return "cem";
        if (n < 1000) return Hundreds[n / 100] + (n % 100 > 0 ? " e " + Integer(n % 100) : "");

        var parts = new List<string>();
        Group(ref n, 1_000_000_000, "bilhão", "bilhões", parts);
        Group(ref n, 1_000_000, "milhão", "milhões", parts);

        if (n >= 1000)
        {
            var thousands = n / 1000;
            parts.Add(thousands == 1 ? "mil" : Integer(thousands) + " mil");
            n %= 1000;
        }

        if (n > 0)
        {
            // "e" antes do último grupo quando ele é < 100 ou múltiplo de 100 (regra do português).
            var connector = n < 100 || n % 100 == 0 ? " e " : ", ";
            return string.Join(", ", parts) + connector + Integer(n);
        }

        return string.Join(", ", parts);
    }

    private static void Group(ref long n, long scale, string singular, string plural, List<string> parts)
    {
        if (n < scale) return;
        var count = n / scale;
        parts.Add(count == 1 ? $"um {singular}" : $"{Integer(count)} {plural}");
        n %= scale;
    }
}
