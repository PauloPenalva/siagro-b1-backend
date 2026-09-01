using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// A regra que separa PLANEJAMENTO de carga real.
/// </summary>
/// <remarks>
/// Estes testes existem por causa de uma regressão silenciosa concreta: antes do ramo
/// <c>Planned</c>, uma carga recém-criada pela Logística (tudo zerado) casava
/// <c>invoiced &lt;= 0</c> e virava <c>Open</c> — passando a aparecer na tela de Faturamento de
/// Expedição com saldo zero, sem erro nenhum. Se alguém remover o primeiro ramo por achá-lo
/// redundante, é aqui que a remoção aparece.
/// </remarks>
public class ShipmentLoadResolveStatusTests
{
    [Fact]
    public void A_load_with_no_volume_is_planned_not_open()
    {
        Assert.Equal(
            ShipmentLoadStatus.Planned,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: decimal.Zero, invoicedQuantity: decimal.Zero));
    }

    [Fact]
    public void Volume_with_nothing_invoiced_is_open()
    {
        Assert.Equal(
            ShipmentLoadStatus.Open,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: decimal.Zero));
    }

    [Fact]
    public void Part_invoiced_is_partially_invoiced()
    {
        Assert.Equal(
            ShipmentLoadStatus.PartiallyInvoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: 40_000m));
    }

    [Fact]
    public void Everything_invoiced_is_invoiced()
    {
        Assert.Equal(
            ShipmentLoadStatus.Invoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: 90_000m));
    }

    /// <summary>
    /// O volume decide, NÃO a contagem de romaneios. Uma carga com 90 toneladas é uma carga
    /// real mesmo que a fixture não tenha criado romaneio — 90 toneladas não são um
    /// planejamento. É por isso que <c>ResolveStatus</c> não recebe <c>hasTransactions</c>.
    /// </summary>
    [Fact]
    public void Volume_without_shipments_is_still_open()
    {
        Assert.Equal(
            ShipmentLoadStatus.Open,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 0.002m, invoicedQuantity: decimal.Zero));
    }

    /// <summary>
    /// Resíduo aceito e documentado: um romaneio de peso bruto zero deixa a carga em
    /// <c>Planned</c>. Inofensivo — não há o que faturar de qualquer forma.
    /// </summary>
    [Fact]
    public void A_shipment_weighing_nothing_leaves_the_load_planned()
    {
        Assert.Equal(
            ShipmentLoadStatus.Planned,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 0.0005m, invoicedQuantity: decimal.Zero));
    }
}
