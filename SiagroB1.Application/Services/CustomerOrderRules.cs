using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services;

/// <summary>
/// Pedido do cliente da linha de saída (xPed até 15 caracteres; nItemPed de 1 a 6 dígitos, sem ser tudo zero — TNItemPed
/// do XSD). Mora num lugar só porque o complemento fiscal do contrato e a gravação direta da linha o alcançam.
/// </summary>
public static class CustomerOrderRules
{
    public static string? NormalizeNumber(string? value)
    {
        var text = Blank(value);
        if (text is { Length: > 15 })
            throw new DefaultException("O pedido do cliente tem no máximo 15 caracteres.");
        return text;
    }

    public static string? NormalizeItem(string? value)
    {
        var text = Blank(value);
        if (text is not null && (text.Length > 6 || !text.All(char.IsAsciiDigit) || text.All(c => c == '0')))
            throw new DefaultException("O item do pedido do cliente tem de 1 a 6 dígitos.");
        return text;
    }

    /// <summary>Valida e normaliza (vazio vira nulo, aparas saem) o pedido gravado na linha.</summary>
    public static void Ensure(SalesInvoiceItem line)
    {
        line.CustomerOrderNumber = NormalizeNumber(line.CustomerOrderNumber);
        line.CustomerOrderItem = NormalizeItem(line.CustomerOrderItem);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
