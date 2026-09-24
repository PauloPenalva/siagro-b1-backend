using System.Text.RegularExpressions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Regra da placa do veículo, que é também a chave de <c>TRUCKS</c> (GAC-1190). A tela limitava o
/// campo a 7 caracteres e não validava nada: "CUD 1H57" digitado com espaço foi gravado como
/// "CUD 1H5", ao lado do "CUD1H57" já existente, e os romaneios se dividiram entre os dois.
///
/// Só aceita placa brasileira — padrão antigo (ABC1234) ou Mercosul (ABC1D23) —, que eram todos os
/// 1.669 veículos da produção Yokotobi em 24/09/2026.
/// </summary>
public static partial class TruckPlateRules
{
    [GeneratedRegex("^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$")]
    private static partial Regex PlatePattern();

    /// <summary>
    /// Maiúsculas, sem espaços nem hífen, e no padrão de placa brasileira. Recusa em vez de
    /// corrigir o que sobrar: cortar ou completar a placa é o que gerou o cadastro duplicado.
    /// </summary>
    public static string Normalize(string? plate)
    {
        var normalized = new string((plate ?? "")
            .Where(c => !char.IsWhiteSpace(c) && c != '-')
            .ToArray())
            .ToUpperInvariant();

        if (normalized.Length == 0)
            throw new DefaultException("Informe a placa do veículo.");

        if (!PlatePattern().IsMatch(normalized))
            throw new DefaultException("Placa inválida. Use o formato ABC1234 ou ABC1D23.");

        return normalized;
    }
}
