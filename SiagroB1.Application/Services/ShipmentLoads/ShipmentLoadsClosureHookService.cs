using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Gancho da SITUAÇÃO da carga (GAC-1171, melhorias): o lugar único de "a entrega ou o status de
/// uma nota da carga mudou, então a carga pode ter virado ou deixado de ser Concluída". Chamado
/// pela Conferência de Entregas, pela confirmação e pelo estorno de confirmação de nota Normal, e
/// pelas três escritas do ticket de descarga (GAC-1171, rateio).
/// </summary>
/// <remarks>
/// <b>Por que grava log:</b> a Concluída (e o caminho de volta) é consequência de um ato do
/// usuário, mas quem escreve o status é o recálculo. Sem a linha "Situação: de → para" assinada
/// por quem agiu, a carga "se concluiria sozinha", sem rastro.
/// <para>
/// <b>Por que NÃO grava movimento:</b> estes caminhos não mudam o saldo da carga (Pendente já
/// consome, e a Conferência não mexe em quantidade faturada). Quem muda o saldo usa o
/// <see cref="ShipmentLoadsBalanceHookService"/>, que recalcula e grava o movimento pelo delta.
/// </para>
/// <para>
/// Sem <c>SaveChanges</c>: quem chama salva, para o status, o carimbo e o log entrarem juntos com
/// o efeito que os causou. Nota sem carga (legada ou avulsa) é no-op, como no gancho de saldo.
/// </para>
/// </remarks>
public class ShipmentLoadsClosureHookService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ApplyAsync(SalesInvoice invoice, string userName)
    {
        var loadKey = await SalesInvoiceOriginResolver.ResolveShipmentLoadKeyAsync(context, invoice);
        if (loadKey is null)
            return;

        await ApplyAsync(loadKey.Value, userName);
    }

    /// <summary>
    /// GAC-1171 (rateio): o ticket de descarga muda a situação da carga (Faturada ↔ Descarregada) sem
    /// passar por nota nenhuma — chega aqui pela chave da carga.
    /// </summary>
    public async Task ApplyAsync(Guid shipmentLoadKey, string userName)
    {
        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);
        if (load is null)
            return;

        var before = load.Status;

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
            context, load.Key, excludedInvoiceKeys: null);

        if (load.Status == before)
            return;

        load.UpdatedBy = userName;

        changeLog.Register(
            load.Key,
            ShipmentLoadChangeLogFields.Status,
            ShipmentLoadChangeLogFields.DescribeStatus(before),
            ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
            userName);
    }
}
