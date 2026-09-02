using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Decide se a COMPOSIÇÃO de uma carga ainda pode mudar — desvincular romaneios ou cancelar a
/// carga. Fonte única de <c>ShipmentLoadsDetachTransactionsService</c> e
/// <c>ShipmentLoadsCancelService</c>.
/// </summary>
/// <remarks>
/// <b>A trava é sobre CONSUMO, não sobre a existência de documentos.</b> A regra anterior
/// recusava se houvesse qualquer documento de saída não-cancelado ligado à carga; depois de uma
/// devolução sobram sempre DOIS (a origem <c>Returned</c> e o retorno <c>Confirmed</c>), e a
/// carga ficava congelada para sempre — mesmo com a devolução tendo zerado o consumo. Uma carga
/// recusada que não pode ser desmontada nem cancelada não tem saída nenhuma pela tela.
/// <para>
/// O que a trava realmente protege é a invariante que o guard de faturamento não vigia: ele
/// valida o que ENTRA (a soma das notas não passa do volume da carga) e nada pode contra o
/// volume ENCOLHER por baixo de notas já emitidas. Logo, o que importa é se ainda há volume
/// consumido — e não quantos papéis existem.
/// </para>
/// <para>
/// <b>Devolução ao ARMAZÉM continua travando, e é deliberado.</b> Ali o consumo comercial também
/// volta a zero, mas o grão já foi creditado no armazém de destino. Soltar os romaneios os
/// devolveria à Montagem, onde entrariam em outra carga e seriam faturados de novo: o mesmo
/// volume vendido duas vezes E creditado num armazém.
/// </para>
/// <para>
/// Decide sobre o saldo RECALCULADO, nunca sobre o persistido — mesmo precedente de
/// <see cref="ShipmentLoadsBillingGuardService"/>: sob drift, ler o persistido barraria uma
/// operação legítima.
/// </para>
/// </remarks>
public class ShipmentLoadsCompositionGuardService(AppDbContext context)
{
    private const decimal Tolerance = 0.001m;

    public async Task EnsureCanChangeCompositionAsync(ShipmentLoad load)
    {
        var invoiced = await ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync(
            context, load.Key, excludedInvoiceKeys: null);

        if (invoiced > Tolerance)
        {
            var live = await context.SalesInvoices
                .AsNoTracking()
                .Where(x => x.ShipmentLoadKey == load.Key &&
                            x.InvoiceType == SalesInvoiceType.Normal &&
                            x.InvoiceStatus != InvoiceStatus.Cancelled)
                .Select(x => new { x.InvoiceNumber, x.InvoiceStatus })
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"A carga {load.Code} ainda tem {invoiced:N3} consumido(s) pelo documento de saída " +
                $"{live?.InvoiceNumber} (situação {live?.InvoiceStatus}) e sua composição não pode " +
                "ser alterada. Cancele ou devolva o documento antes.");
        }

        var returned = await ShipmentLoadsRecalculateReturnedService
            .CalculateReturnedToWarehouseAsync(context, load.Key);

        if (returned > Tolerance)
        {
            throw new ApplicationException(
                $"A carga {load.Code} teve {returned:N3} devolvido(s) ao armazém por recusa e sua " +
                "composição não pode mais ser alterada.");
        }
    }
}
