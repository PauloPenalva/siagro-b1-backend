using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Trava da carga com recusa aguardando NF-e de entrada (spec 2026-10-09 §6). Uma regra e uma mensagem para todas
/// as operações que mexem em saldo, ticket ou transbordo — quem lê a mensagem precisa saber os dois caminhos.
/// </summary>
public static class ShipmentLoadRefusalRules
{
    /// <summary>GAC-1213: o romaneio de quebra de trajeto grava o armazém de origem da carga (coluna NOT NULL).</summary>
    public static string MissingOriginWarehouseMessage(ShipmentLoad load) =>
        $"A carga {load.Code} não tem armazém de origem informado; a quebra de trajeto não pode ser registrada.";

    public static string PendingMessage(string? loadCode) =>
        $"A carga {loadCode} tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa.";

    public static void EnsureNoPendingRefusal(ShipmentLoad load)
    {
        if (load.Status == ShipmentLoadStatus.RefusalPending)
            throw new DefaultException(PendingMessage(load.Code));
    }

    /// <summary>
    /// GAC-1226: a quantidade FÍSICA que a recusa move — a devolvida (quantidade da NF) mais o ajuste do efetivo dos
    /// documentos divergentes devolvidos por inteiro (<see cref="SalesInvoiceDivergenceRules.PhysicalAdjustmentAsync"/>).
    /// </summary>
    /// <remarks>
    /// Só Warehouse, Transshipment e TransitLoss movem mercadoria. No "segue viagem" nada se move: a carga volta ao
    /// faturamento pelo saldo, e o termo de divergência sai sozinho quando o documento fica totalmente devolvido.
    /// As devoluções, o devolvido da origem e o contrato continuam na quantidade da NF — só o físico muda aqui.
    /// </remarks>
    public static async Task<decimal> PhysicalQuantityAsync(
        AppDbContext context,
        RefusalDestination destination,
        decimal returnedQuantity,
        IReadOnlyCollection<(Guid SalesInvoiceKey, decimal RemainingAfterRefusal)> refusedDocuments)
    {
        if (destination is not (RefusalDestination.Warehouse or RefusalDestination.Transshipment
            or RefusalDestination.TransitLoss))
            return returnedQuantity;

        var adjustment = await SalesInvoiceDivergenceRules.PhysicalAdjustmentAsync(context, refusedDocuments);
        var physical = decimal.Round(returnedQuantity + adjustment, 3, MidpointRounding.ToEven);

        // Anomalia de dado: um registro de divergência negativa maior que o próprio documento zeraria (ou inverteria)
        // o que se move. Não se movimenta quantidade não positiva — fica a devolvida, sem ajuste.
        return physical > decimal.Zero ? physical : returnedQuantity;
    }
}
