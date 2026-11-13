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
                totalQuantity: decimal.Zero, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    [Fact]
    public void Volume_with_nothing_invoiced_is_open()
    {
        Assert.Equal(
            ShipmentLoadStatus.Open,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    [Fact]
    public void Part_invoiced_is_partially_invoiced()
    {
        Assert.Equal(
            ShipmentLoadStatus.PartiallyInvoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: 40_000m, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    [Fact]
    public void Everything_invoiced_is_invoiced()
    {
        Assert.Equal(
            ShipmentLoadStatus.Invoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: 90_000m, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
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
                totalQuantity: 0.002m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
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
                totalQuantity: 0.0005m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    // ─── Terceiro termo: mercadoria recusada e devolvida a um armazém ───

    /// <summary>
    /// Recusa TOTAL com retorno ao armazém: nada faturado, tudo devolvido. Sem o termo no
    /// consumo isto casaria <c>consumed &lt;= 0</c> e a carga voltaria a <c>Open</c>, oferecendo
    /// ao Faturamento de Expedição um volume que já está creditado em outro armazém.
    /// </summary>
    [Fact]
    public void Everything_returned_to_a_warehouse_closes_the_load_as_returned()
    {
        Assert.Equal(
            ShipmentLoadStatus.Returned,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 90_000m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: 90_000m, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    /// <summary>
    /// Carga MISTA — parte entregue e faturada, parte recusada e devolvida ao armazém. Os dois
    /// abatimentos somam o total, então a carga está encerrada.
    /// </summary>
    [Fact]
    public void Part_invoiced_plus_part_returned_closes_the_load_as_returned()
    {
        Assert.Equal(
            ShipmentLoadStatus.Returned,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 40_000m, invoicedQuantity: 25_000m, returnedToWarehouseQuantity: 15_000m, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    /// <summary>
    /// O rótulo <c>Returned</c> prevalece sobre <c>Invoiced</c> na carga mista: esconder o
    /// retorno físico na lista é pior do que a carga aparecer como "Devolvida" — a tela de
    /// Detalhe mostra as três quantidades lado a lado.
    /// </summary>
    [Fact]
    public void A_closed_load_without_any_return_is_still_invoiced()
    {
        Assert.Equal(
            ShipmentLoadStatus.Invoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 40_000m, invoicedQuantity: 40_000m, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    /// <summary>
    /// Recusa PARCIAL ao armazém deixa saldo: 15 de 40 voltaram, 25 ainda podem ser faturados.
    /// </summary>
    [Fact]
    public void Part_returned_to_a_warehouse_leaves_the_rest_available()
    {
        Assert.Equal(
            ShipmentLoadStatus.PartiallyInvoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 40_000m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: 15_000m, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    /// <summary>
    /// A tolerância de fechamento vale para a SOMA dos dois abatimentos, não para cada um.
    /// </summary>
    [Fact]
    public void The_closing_tolerance_applies_to_the_sum_of_both_deductions()
    {
        Assert.Equal(
            ShipmentLoadStatus.Returned,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 40_000m, invoicedQuantity: 25_000m, returnedToWarehouseQuantity: 14_999.9995m, transshippedQuantity: decimal.Zero, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    // ─── Quarto termo: transbordo (GAC-1181) — o rótulo olha só o consumo COMERCIAL ───

    /// <summary>
    /// Regressão corrigida: depois que a saída do transbordo é vinculada, o volume transbordado
    /// fecha o SALDO da carga, mas nada foi faturado ainda — o rótulo não pode ler "Faturada
    /// Parcial" com faturado zero. É o cenário relatado pelo usuário (carga com 99.500 no total,
    /// 50.000 transbordados e 0 faturado lia "Faturada Parcial").
    /// </summary>
    [Fact]
    public void Transshipment_output_linked_with_nothing_invoiced_is_open()
    {
        Assert.Equal(
            ShipmentLoadStatus.Open,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 99_500m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: 50_000m, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    /// <summary>
    /// O restante da carga, depois do transbordo, é faturado por completo: saldo fecha e o
    /// rótulo é <c>Invoiced</c>, sem devolução ao armazém.
    /// </summary>
    [Fact]
    public void Everything_invoiced_after_the_transshipment_is_invoiced()
    {
        Assert.Equal(
            ShipmentLoadStatus.Invoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 99_500m, invoicedQuantity: 49_500m, returnedToWarehouseQuantity: decimal.Zero, transshippedQuantity: 50_000m, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    /// <summary>
    /// O restante da carga, depois do transbordo, volta inteiro ao armazém (recusa/devolução):
    /// saldo fecha e o rótulo é <c>Returned</c>, que prevalece sobre <c>Invoiced</c>.
    /// </summary>
    [Fact]
    public void Transshipment_plus_warehouse_return_closes_the_load_as_returned()
    {
        Assert.Equal(
            ShipmentLoadStatus.Returned,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: 99_500m, invoicedQuantity: decimal.Zero, returnedToWarehouseQuantity: 49_500m, transshippedQuantity: 50_000m, transitLossQuantity: decimal.Zero, hasOpenTransshipment: false));
    }

    // ─── Quinto termo: quebra de trajeto (GAC-1213) ───

    [Theory]
    [InlineData(40_000, 0, 0, 0, 40_000, ShipmentLoadStatus.TransitLoss)]        // perda total
    [InlineData(40_000, 25_000, 0, 0, 15_000, ShipmentLoadStatus.TransitLoss)]   // mista: entregue + perdida
    [InlineData(40_000, 0, 20_000, 0, 20_000, ShipmentLoadStatus.TransitLoss)]   // quebra vence Devolvida
    [InlineData(40_000, 0, 0, 0, 10_000, ShipmentLoadStatus.PartiallyInvoiced)]  // saldo aberto: quebra conta como consumo
    [InlineData(40_000, 40_000, 0, 0, 0, ShipmentLoadStatus.Invoiced)]           // sem quebra, nada muda
    public void ResolveStatus_WithTransitLoss(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, decimal loss,
        ShipmentLoadStatus expected)
    {
        Assert.Equal(expected, ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
            total, invoiced, returned, transshipped, loss, hasOpenTransshipment: false));
    }

    // ─── Transferência entre armazéns de terceiros (GAC-1176): o último transbordo é destino final ───

    [Theory]
    [InlineData(30_000, 0, 0, 30_000, 0, true, ShipmentLoadStatus.Completed)]        // só transferência, nunca faturada
    [InlineData(30_000, 10_000, 0, 20_000, 0, true, ShipmentLoadStatus.Invoiced)]    // parte faturada: segue o comercial (Conferência refina)
    [InlineData(30_000, 0, 10_000, 20_000, 0, true, ShipmentLoadStatus.Returned)]    // devolução vence
    [InlineData(30_000, 0, 0, 20_000, 10_000, true, ShipmentLoadStatus.TransitLoss)] // quebra de trajeto vence
    [InlineData(30_000, 0, 0, 30_000, 0, false, ShipmentLoadStatus.Invoiced)]        // sem o fato, nada muda
    public void ResolveStatus_WithFinalTransfer(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, decimal loss,
        bool endsAtFinalTransshipment, ShipmentLoadStatus expected)
    {
        Assert.Equal(expected, ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
            total, invoiced, returned, transshipped, loss, hasOpenTransshipment: false,
            endsAtFinalTransshipment: endsAtFinalTransshipment));
    }
}
