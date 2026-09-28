using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Regras compartilhadas pelos três serviços de escrita do ticket de descarga (GAC-1171). Desde o
/// rateio, também a validação das parcelas e a elegibilidade das linhas.
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

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Peso em 3 casas, a escala de <c>DECIMAL(18,3)</c>.</summary>
    public static decimal RoundQuantity(decimal quantity) =>
        decimal.Round(quantity, 3, MidpointRounding.ToEven);

    /// <summary>
    /// Normaliza e valida o rateio (GAC-1171, rateio): arredonda cada parcela em 3 casas, recusa
    /// parcela negativa e item repetido, descarta as parcelas zero, exige ao menos uma, e exige que
    /// a soma feche com o peso do ticket dentro de <see cref="Tolerance"/>.
    /// </summary>
    /// <returns>As parcelas maiores que zero, na ordem recebida.</returns>
    public static IReadOnlyList<ShipmentLoadDischargeLine> NormalizeDistribution(
        decimal ticketQuantity, IReadOnlyList<ShipmentLoadDischargeLine>? lines)
    {
        var rounded = (lines ?? [])
            .Select(line => line with { Quantity = RoundQuantity(line.Quantity) })
            .ToList();

        if (rounded.Any(line => line.Quantity < decimal.Zero))
            throw new DefaultException("O peso rateado não pode ser negativo.");

        if (rounded.GroupBy(line => line.SalesInvoiceItemKey).Any(group => group.Count() > 1))
            throw new DefaultException("O mesmo item de documento de saída aparece duas vezes no rateio.");

        var positive = rounded.Where(line => line.Quantity > decimal.Zero).ToList();

        if (positive.Count == 0)
            throw new DefaultException("Distribua o peso descarregado entre os documentos de saída.");

        var distributed = positive.Sum(line => line.Quantity);

        if (Math.Abs(distributed - ticketQuantity) > Tolerance)
            throw new DefaultException(
                $"O rateio ({distributed.ToString("N3", PtBr)}) não fecha com o peso descarregado " +
                $"({RoundQuantity(ticketQuantity).ToString("N3", PtBr)}).");

        return positive;
    }

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
    /// Carga cancelada está congelada: as três operações mexem em quantidade. Nas demais situações
    /// quem decide é a elegibilidade das linhas (<see cref="ResolveLinesAsync"/>) — é o que deixa a
    /// carga mista "Devolvida" registrar o ticket da parte que foi entregue (GAC-1171, rateio, D6).
    /// </summary>
    public static void EnsureLoadAcceptsChanges(ShipmentLoad load)
    {
        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new DefaultException("Carga cancelada não aceita registro de descarga.");
    }

    /// <summary>
    /// Resolve no servidor a nota de cada parcela e confere a elegibilidade: nota desta carga,
    /// Normal, Confirmada, e com faturado que não voltou. A tela manda só a linha — o par nota/linha
    /// nunca é aceito dela.
    /// </summary>
    /// <returns>
    /// As parcelas prontas para o ticket, na ordem recebida, com as navegações <c>SalesInvoice</c> e
    /// <c>SalesInvoiceItem</c> apontando as instâncias rastreadas (o log usa o número da nota).
    /// </returns>
    public static async Task<IReadOnlyList<ShipmentLoadDischargeItem>> ResolveLinesAsync(
        AppDbContext context, Guid loadKey, IReadOnlyList<ShipmentLoadDischargeLine> lines)
    {
        var keys = lines.Select(line => (Guid?)line.SalesInvoiceItemKey).ToList();

        var items = await context.SalesInvoicesItems
            .Include(x => x.SalesInvoice)
            .Where(x => keys.Contains(x.Key))
            .ToListAsync();

        var result = new List<ShipmentLoadDischargeItem>();

        foreach (var line in lines)
        {
            var item = items.FirstOrDefault(x => x.Key == line.SalesInvoiceItemKey)
                ?? throw new NotFoundException("Item do documento de saída não encontrado.");

            var invoice = item.SalesInvoice
                ?? throw new NotFoundException("Documento de saída não encontrado.");

            var number = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "(sem número)" : invoice.InvoiceNumber;

            if (invoice.ShipmentLoadKey != loadKey)
                throw new DefaultException($"O documento de saída {number} não pertence a esta carga.");

            if (invoice.InvoiceType != SalesInvoiceType.Normal)
                throw new DefaultException($"O documento {number} é de devolução e não recebe descarga.");

            if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
                throw new DefaultException(
                    $"O documento de saída {number} não está confirmado. Só documento confirmado recebe descarga.");

            if (RemainingQuantity(item) <= Tolerance)
                throw new DefaultException(
                    $"O documento de saída {number} foi devolvido por inteiro e não recebe descarga.");

            result.Add(new ShipmentLoadDischargeItem
            {
                SalesInvoiceKey = invoice.Key,
                SalesInvoice = invoice,
                SalesInvoiceItemKey = item.Key!.Value,
                SalesInvoiceItem = item,
                Quantity = line.Quantity,
            });
        }

        return result;
    }
}
