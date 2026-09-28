using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Regras compartilhadas pelos três serviços de escrita do ticket de descarga (GAC-1171).
/// </summary>
/// <remarks>
/// ⚠️ NÃO existe aqui guard de "entrega encerrada", e a ausência é deliberada: o ticket é aceito
/// em qualquer estado da conferência, porque ele não escreve no número conferido. Barrar deixaria
/// a Logística sem onde lançar o documento de que o financeiro precisa para liberar o frete.
/// </remarks>
public static class ShipmentLoadDischargeRules
{
    /// <summary>Tolerância de fechamento, a mesma casa decimal das quantidades.</summary>
    public const decimal Tolerance = 0.001m;

    /// <summary>
    /// Faturado que não voltou: <c>Quantity − ReturnedQuantity</c>, em 3 casas. É a base do rateio e
    /// o que torna a linha elegível ao ticket (GAC-1171, rateio).
    /// </summary>
    /// <remarks>
    /// ⚠️ Não é <see cref="SalesInvoiceItem.NetQuantity"/>, que é o conferido menos a quebra.
    /// </remarks>
    public static decimal RemainingQuantity(SalesInvoiceItem item) =>
        decimal.Round(item.Quantity - item.ReturnedQuantity, 3, MidpointRounding.ToEven);

    public static string NormalizeTicketNumber(string? ticketNumber)
    {
        var text = (ticketNumber ?? string.Empty).Trim();

        if (text.Length == 0)
            throw new DefaultException("Informe o número do ticket de descarga.");

        return text.Length > 50 ? text[..50] : text;
    }

    public static void EnsurePositiveQuantity(decimal quantity)
    {
        if (quantity <= decimal.Zero)
            throw new DefaultException("O peso descarregado deve ser maior que zero.");
    }

    /// <summary>
    /// Carga cancelada ou devolvida está congelada: as três operações mexem em quantidade.
    /// Diferente do comentário da carga, que vale a qualquer tempo porque não move número nenhum.
    /// </summary>
    public static void EnsureLoadAcceptsChanges(ShipmentLoad load)
    {
        if (load.Status is ShipmentLoadStatus.Cancelled or ShipmentLoadStatus.Returned)
            throw new DefaultException(
                "Carga cancelada ou devolvida não aceita registro de descarga.");
    }
}
