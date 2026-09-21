using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O quarto termo do saldo da carga (GAC-1181) — o volume que saiu para um armazém
/// intermediário — e a situação <see cref="ShipmentLoadStatus.InTransshipment"/> que ele cria.
/// </summary>
/// <remarks>
/// Ninguém cria linha de transbordo ainda (Tasks 4-6 são os serviços de iniciar/registrar
/// entrada/estornar), então as linhas aqui são montadas direto no InMemory.
/// </remarks>
public class ShipmentLoadTransshipmentBalanceTests
{
    // total, invoiced, returned, transshipped, hasOpen, esperado
    [Theory]
    [InlineData(0, 0, 0, 0, false, ShipmentLoadStatus.Planned)]
    [InlineData(30, 0, 0, 0, false, ShipmentLoadStatus.Open)]
    [InlineData(30, 30, 0, 0, false, ShipmentLoadStatus.Invoiced)]
    [InlineData(30, 0, 0, 30, true, ShipmentLoadStatus.InTransshipment)]
    [InlineData(59.5, 0, 0, 30, false, ShipmentLoadStatus.PartiallyInvoiced)]
    [InlineData(59.5, 29.5, 0, 30, false, ShipmentLoadStatus.Invoiced)]
    [InlineData(40, 0, 40, 0, false, ShipmentLoadStatus.Returned)]
    public void ResolveStatus_ConsidersTheTransshipment(
        decimal total, decimal invoiced, decimal returned, decimal transshipped,
        bool hasOpen, ShipmentLoadStatus expected)
    {
        Assert.Equal(expected, ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
            total, invoiced, returned, transshipped, hasOpen));
    }

    /// <summary>
    /// O ramo do transbordo vem PRIMEIRO, antes até do ramo <c>Planned</c>: uma carga com saldo
    /// zero porque tudo foi para o armazém intermediário não pode ler "Planejada".
    /// </summary>
    [Fact]
    public void An_open_transshipment_wins_even_over_a_load_with_no_volume()
    {
        Assert.Equal(
            ShipmentLoadStatus.InTransshipment,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                totalQuantity: decimal.Zero,
                invoicedQuantity: decimal.Zero,
                returnedToWarehouseQuantity: decimal.Zero,
                transshippedQuantity: decimal.Zero,
                hasOpenTransshipment: true));
    }

    /// <summary>
    /// Cenário do usuário: SP → porto (transbordo) → recusa/padronização → parceiro → cliente 2.
    /// Só a aritmética — as linhas de romaneio e transbordo são montadas direto no InMemory.
    /// </summary>
    [Fact]
    public async Task The_load_balance_accounts_for_the_full_round_trip_through_the_transshipment()
    {
        var db = TestDb.CreateUnitOfWork();

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000099",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 30_000m,
        };
        db.Context.ShipmentLoads.Add(load);

        // Perna 1: embarque em SP, direto para o armazém intermediário (porto).
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R001",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            GrossWeight = 30_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        });

        // O transbordo descarrega o saldo INTEIRO da carga no armazém intermediário.
        var transshipment = new ShipmentLoadTransshipment
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = "ARM02",
            OutgoingQuantity = 30_000m,
        };
        db.Context.ShipmentLoadsTransshipments.Add(transshipment);

        // Perna 2: saída do armazém intermediário para o cliente 2, com quebra de 500 kg.
        // Continua a MESMA carga — ShipmentLoadKey preenchido — e aponta o transbordo de origem.
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R002",
            CardCode = "C002",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM02",
            GrossWeight = 29_500m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentLoadTransshipmentKey = transshipment.Key,
        });

        await db.Context.SaveChangesAsync();

        await ShipmentLoadsRecalculateTotalService.RecalculateAsync(db.Context, load.Key);

        load.TransshippedQuantity = await ShipmentLoadsRecalculateTransshippedService
            .CalculateTransshippedAsync(db.Context, load.Key);

        Assert.Equal(59_500m, load.TotalQuantity);
        Assert.Equal(30_000m, load.TransshippedQuantity);
        Assert.Equal(29_500m, load.AvailableQuantity);
    }
}
