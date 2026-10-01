using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Escritor ÚNICO de <c>SalesInvoiceItem.ReturnedQuantity</c> e <c>SalesInvoice.ReturnedQuantity</c>
/// (GAC-1171, rateio): quanto de cada linha da nota de ORIGEM já voltou em devoluções CONFIRMADAS.
/// </summary>
/// <remarks>
/// Estático e sem <c>SaveChanges</c>, como <c>SalesInvoicesReturnOriginRestoreService</c>: roda dentro
/// da transação de quem confirma ou estorna a devolução, e o valor entra no mesmo <c>SaveChanges</c>
/// do status que o causou.
/// <para>
/// ⚠️ Lê as devoluções RASTREADAS e filtra o status EM MEMÓRIA. Quem chama acabou de trocar o status
/// da devolução e ainda não salvou: com o status no WHERE, o banco devolveria o valor antigo. A
/// consulta rastreada devolve a instância já rastreada com o valor atual, e o filtro em memória
/// enxerga a troca — mesma defesa de <c>ShipmentLoadsRecalculateInvoicedService.EvaluateClosureAsync</c>.
/// </para>
/// <para>
/// Cancelar e excluir uma devolução não chamam este serviço, e não precisam: os dois só alcançam
/// devolução NÃO confirmada (<c>SalesInvoicesCancelService</c> recusa retorno confirmado), que não
/// entra na soma.
/// </para>
/// </remarks>
public static class SalesInvoicesRecalculateReturnedService
{
    public static async Task RecalculateAsync(AppDbContext context, Guid? originInvoiceKey)
    {
        if (originInvoiceKey is null)
            return;

        var origin = await context.SalesInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == originInvoiceKey);

        if (origin is null)
            return;

        var returns = await context.SalesInvoices
            .Include(x => x.Items)
            .Where(x => x.InvoiceType == SalesInvoiceType.Return
                        && x.SalesInvoiceOriginKey == originInvoiceKey)
            .ToListAsync();

        var confirmedReturnItems = returns
            .Where(x => context.Entry(x).State != EntityState.Deleted
                        && x.InvoiceStatus == InvoiceStatus.Confirmed)
            .SelectMany(x => x.Items)
            .Where(x => context.Entry(x).State != EntityState.Deleted)
            .ToList();

        foreach (var item in origin.Items)
        {
            item.ReturnedQuantity = decimal.Round(
                confirmedReturnItems
                    .Where(x => x.SalesInvoiceItemOriginKey == item.Key)
                    .Sum(x => x.Quantity),
                3, MidpointRounding.ToEven);
        }

        origin.ReturnedQuantity = decimal.Round(
            origin.Items.Sum(x => x.ReturnedQuantity), 3, MidpointRounding.ToEven);
    }
}
