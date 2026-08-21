using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Gancho único do saldo da carga, chamado por todo serviço que muda o estado de um documento
/// de saída. Recalcula o saldo e grava o movimento correspondente numa operação só.
/// </summary>
/// <remarks>
/// <b>Por que um serviço, e não cinco cópias do mesmo trecho:</b> a fórmula do saldo da
/// liberação de venda chegou a existir em QUATRO lugares, e uma das cópias ficou para trás numa
/// regra antiga por meses, sem ninguém notar. Aqui o cálculo, o snapshot e o movimento moram
/// num lugar só.
/// <para>
/// <b>Onde chamar:</b> no serviço que APLICA o efeito, depois do <c>SaveChangesAsync</c> que
/// torna o efeito visível à consulta e antes do <c>CommitAsync</c> — a projeção do saldo soma
/// as notas no SERVIDOR e leria o estado anterior se rodasse antes do flush.
/// </para>
/// <para>
/// <b>O movimento é assinado pelo DELTA real</b>, não por um sinal fixo por tipo: cancelar uma
/// nota normal devolve saldo (+) e cancelar uma devolução re-consome (−), e é o mesmo serviço
/// que dispara os dois. Derivar do antes/depois evita ter de acertar o sinal em cada chamada.
/// </para>
/// </remarks>
public class ShipmentLoadsBalanceHookService(
    AppDbContext context,
    ShipmentLoadsMovementLogService movementLog)
{
    /// <summary>
    /// Recalcula o saldo da carga afetada pelo documento e registra o movimento.
    /// No-op quando o documento não pertence a carga nenhuma — que é o caso de todo documento
    /// legado e avulso, e a razão de o gancho poder ser chamado incondicionalmente.
    /// </summary>
    /// <param name="excludedInvoiceKeys">
    /// Notas a excluir da soma. Necessário quando a nota é REMOVIDA na mesma transação: o
    /// <c>SumAsync</c> agrega no servidor e ainda a enxergaria.
    /// </param>
    public async Task ApplyAsync(
        SalesInvoice invoice,
        ShipmentLoadMovementType movementType,
        string userName,
        string description,
        ICollection<Guid>? excludedInvoiceKeys = null)
    {
        var loadKey = await ResolveLoadKeyAsync(invoice);

        if (loadKey == null)
            return;

        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey.Value);

        if (load == null || load.Status == ShipmentLoadStatus.Cancelled)
            return;

        var balanceBefore = load.AvailableQuantity;

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
            context, loadKey.Value, excludedInvoiceKeys);

        var balanceAfter = load.AvailableQuantity;

        movementLog.Register(
            loadKey.Value,
            movementType,
            decimal.Round(balanceAfter - balanceBefore, 3, MidpointRounding.ToEven),
            balanceAfter,
            description,
            userName,
            invoice.Key,
            invoice.InvoiceNumber);
    }

    /// <summary>
    /// A carga afetada pelo documento. Normalmente é a própria: <c>SalesInvoiceCopyFactory</c>
    /// copia <c>ShipmentLoadKey</c> para a devolução, de modo que retorno e origem apontem a
    /// mesma carga. O fallback pela origem cobre devoluções criadas ANTES dessa cópia existir.
    /// </summary>
    private async Task<Guid?> ResolveLoadKeyAsync(SalesInvoice invoice)
    {
        if (invoice.ShipmentLoadKey is { } own)
            return own;

        if (invoice.InvoiceType != SalesInvoiceType.Return || invoice.SalesInvoiceOriginKey == null)
            return null;

        return await context.SalesInvoices
            .Where(x => x.Key == invoice.SalesInvoiceOriginKey.Value)
            .Select(x => x.ShipmentLoadKey)
            .FirstOrDefaultAsync();
    }
}
