using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Numa DEVOLUÇÃO o peso do cabeçalho é DERIVADO: vale a soma das quantidades dos itens.
/// </summary>
/// <remarks>
/// <b>Por que só na devolução.</b> Num documento normal os dois números medem coisas
/// diferentes e podem divergir de direito: o peso é o da balança e a quantidade é a faturada
/// (tara, quebra, arredondamento de nota). Numa devolução não há esse espaço — o que voltou é
/// um número só, e quem o lê a jusante é sempre a <c>Quantity</c> do item: o ledger de
/// alocações (<c>SalesContractsAllocationCreateForReturnService</c>), o saldo da liberação e o
/// fechamento da origem (<c>IsFullyReturnedAsync</c>). O peso do cabeçalho não entra em conta
/// em lugar nenhum.
/// <para>
/// ⚠️ <b>O defeito que criou esta regra</b> (homologação Yokotobi, 28/08/2026): o operador abriu
/// a devolução pendente, digitou 20 no Peso Líquido e confirmou. A linha continuou com os 30 da
/// origem e o contrato recebeu 30 de volta — dois campos dizendo "quanto voltou", só um
/// decidindo, e nada avisando que discordavam. Derivar o peso fecha a divergência na origem;
/// <see cref="EnsureHeaderWeightMatchesItems"/> é a segunda parede, para quem chega pela API ou
/// carrega dado legado.
/// </para>
/// </remarks>
public static class SalesInvoicesReturnWeightService
{
    /// <summary>Tolerância de comparação, na mesma casa decimal das quantidades.</summary>
    public const decimal Tolerance = 0.001m;

    /// <summary>Cultura dos números que vão para texto lido pelo operador.</summary>
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static decimal SumQuantities(IEnumerable<SalesInvoiceItem> items) =>
        decimal.Round(items.Sum(i => i.Quantity), 3, MidpointRounding.ToEven);

    /// <summary>
    /// Reescreve os pesos da devolução a partir dos itens que ela carrega em memória. No-op em
    /// documento que não é devolução.
    /// </summary>
    public static void Apply(SalesInvoice invoice)
    {
        if (invoice.InvoiceType != SalesInvoiceType.Return)
            return;

        var quantity = SumQuantities(invoice.Items);

        invoice.GrossWeight = quantity;
        invoice.NetWeight = quantity;
    }

    /// <summary>
    /// Reescreve os pesos da devolução relendo os itens do banco — o caminho dos serviços que
    /// mexem em UM item e não têm a nota em mãos. Sem <c>SaveChanges</c>: quem chama persiste.
    /// </summary>
    /// <remarks>
    /// A soma agrega no SERVIDOR, então o chamador precisa ter flushado a mutação do item antes
    /// de chegar aqui — senão esta conta lê o estado anterior.
    /// </remarks>
    public static async Task RecalculateAsync(AppDbContext context, Guid? salesInvoiceKey)
    {
        if (salesInvoiceKey is not { } key)
            return;

        var invoice = await context.SalesInvoices.FirstOrDefaultAsync(x => x.Key == key);

        if (invoice is null || invoice.InvoiceType != SalesInvoiceType.Return)
            return;

        var quantity = await context.SalesInvoicesItems
            .Where(x => x.SalesInvoiceKey == key)
            .SumAsync(x => (decimal?)x.Quantity) ?? decimal.Zero;

        quantity = decimal.Round(quantity, 3, MidpointRounding.ToEven);

        invoice.GrossWeight = quantity;
        invoice.NetWeight = quantity;
    }

    /// <summary>
    /// Recusa a devolução cujo peso de cabeçalho não bate com a soma dos itens, nomeando os
    /// DOIS números — o operador precisa saber qual dos campos está mentindo.
    /// </summary>
    public static void EnsureHeaderWeightMatchesItems(SalesInvoice invoice)
    {
        if (invoice.InvoiceType != SalesInvoiceType.Return)
            return;

        var quantity = SumQuantities(invoice.Items);

        if (Math.Abs(invoice.NetWeight - quantity) <= Tolerance)
            return;

        throw new ApplicationException(
            $"O peso líquido do documento de devolução {invoice.InvoiceNumber} " +
            $"({invoice.NetWeight.ToString("N3", PtBr)}) não confere com a soma das quantidades " +
            $"dos itens ({quantity.ToString("N3", PtBr)}). Quem devolve saldo ao contrato é a " +
            "quantidade dos itens — corrija a quantidade na linha, não o peso do cabeçalho.");
    }
}
