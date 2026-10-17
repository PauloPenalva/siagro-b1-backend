using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Validações comuns dos cadastros de ICMS. A calculadora do DIFAL recusa alíquota somada de 100 % ou mais, então o
/// cadastro barra isso na hora de salvar: o documento nunca pode travar depois por um cadastro aceito aqui.
/// </summary>
internal static class IcmsRateValidation
{
    /// <summary>UF em sigla de 2 letras, normalizada em maiúsculas.</summary>
    internal static string NormalizeState(string? stateCode)
    {
        var state = (stateCode ?? string.Empty).Trim().ToUpperInvariant();

        if (state.Length != 2 || !state.All(char.IsAsciiLetter))
            throw new DefaultException("Informe a UF com a sigla de 2 letras (ex.: SP).");

        return state;
    }

    internal static void ValidateRates(decimal internalRate, decimal? fcpRate)
    {
        if (internalRate is < 0 or > 100)
            throw new DefaultException("A alíquota interna deve estar entre 0 e 100.");

        if (fcpRate is < 0 or > 100)
            throw new DefaultException("O FCP deve estar entre 0 e 100.");

        if (internalRate + (fcpRate ?? 0) >= 100)
            throw new DefaultException("A soma da alíquota interna com o FCP deve ser menor que 100.");
    }
}
