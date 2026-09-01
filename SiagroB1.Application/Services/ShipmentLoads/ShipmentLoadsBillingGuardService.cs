using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Invariante I2 — <b>Σ das notas vivas ≤ TotalQuantity da carga</b>. Roda ANTES de qualquer
/// escrita: uma tentativa recusada não pode deixar efeito no banco.
/// </summary>
/// <remarks>
/// <b>Não existe CHECK nem índice que force I2</b> — é soma sobre outra tabela. Esta classe é
/// a única camada lógica, e ela NÃO protege contra concorrência: duas notas parciais
/// simultâneas passam pelos dois guards. Quem derruba a segunda é o
/// <c>[Timestamp] ShipmentLoad.RowVersion</c> no <c>SaveChanges</c>. Faturamento parcial
/// convida ao paralelismo, então isso não é teórico.
/// <para>
/// Decide sobre o saldo RECALCULADO, nunca sobre o persistido: sob drift, ler o persistido
/// barraria um faturamento legítimo. Mesmo precedente de
/// <c>SalesShipmentReleasesCloseService</c>.
/// </para>
/// <para>
/// Este guard é sobre volume FÍSICO da carga. Ele não tem parentesco com saldo de contrato —
/// o faturamento continua sem validar saldo comercial, conforme o <c>&lt;remarks&gt;</c> de
/// <c>ShipmentBillingCreateSalesInvoiceService</c>.
/// </para>
/// </remarks>
public class ShipmentLoadsBillingGuardService(AppDbContext context)
{
    /// <summary>
    /// Tolerância de um milésimo: quantidade é <c>DECIMAL(18,3)</c> e um arredondamento no
    /// cliente não pode impedir o faturamento do último milésimo da carga.
    /// </summary>
    private const decimal Tolerance = 0.001m;

    public async Task EnsureCanBillAsync(Guid shipmentLoadKey, decimal quantity)
    {
        if (quantity <= decimal.Zero)
            throw new ApplicationException("Informe uma quantidade a faturar maior que zero.");

        var load = await context.ShipmentLoads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        if (load == null)
            throw new ApplicationException("Carga não encontrada.");

        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException($"A carga {load.Code} está cancelada e não pode ser faturada.");

        // Recusa por STATUS, não pela comparação de saldo abaixo. A carga planejada tem volume
        // zero e cairia lá de qualquer jeito, mas com a mensagem errada ("quantidade maior que
        // o saldo... Total da carga: 0,000"), que manda o usuário procurar um problema de
        // quantidade quando o que falta é vincular romaneio.
        if (load.Status == ShipmentLoadStatus.Planned)
            throw new ApplicationException(
                $"A carga {load.Code} ainda está apenas planejada. Vincule os romaneios de " +
                "embarque antes de faturá-la.");

        var invoiced = await ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync(
            context, shipmentLoadKey, excludedInvoiceKeys: null);

        var available = ShipmentLoad.CalculateAvailableQuantity(load.TotalQuantity, invoiced);

        if (quantity > available + Tolerance)
            throw new ApplicationException(
                $"Quantidade a faturar ({quantity:N3}) maior que o saldo da carga {load.Code} " +
                $"({available:N3}). Total da carga: {load.TotalQuantity:N3}, já faturado: {invoiced:N3}.");
    }
}
