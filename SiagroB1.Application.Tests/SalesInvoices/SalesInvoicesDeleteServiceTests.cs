using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Exclusão do documento de saída Pendente.</summary>
public class SalesInvoicesDeleteServiceTests
{
    private static SalesInvoicesDeleteService Service(UnitOfWork db) => new(db,
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesDeleteService>.Instance);

    /// <remarks>
    /// GAC-1226: o registro de divergência sai pelo cascade da FK, sem código no serviço. O InMemory só aplica o
    /// cascade a dependentes RASTREADOS, e o serviço não carrega o registro; por isso o arranjo o carrega no contexto
    /// do serviço — no SQL Server o cascade da FK o apaga de qualquer forma. O saldo da carga é o que importa: ele
    /// volta inteiro mesmo sem o cascade, porque o documento removido sai do termo de divergência pelos excluídos.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_pending_divergent_document_removes_the_record_and_restores_the_balance()
    {
        var name = Guid.NewGuid().ToString();
        SalesInvoiceDivergenceScenario scenario;
        await using (var seed = new AppDbContext(TestDb.Options(name)))
            scenario = await SalesInvoiceDivergenceTestSeed.SeedAsync(seed, InvoiceStatus.Pending);
        var db = TestDb.CreateUnitOfWork(name);
        await db.Context.SalesInvoiceDivergences.LoadAsync();

        Assert.True(await Service(db).ExecuteAsync(scenario.InvoiceKey, "tester"));

        await using var fresh = new AppDbContext(TestDb.Options(name));
        Assert.False(await fresh.SalesInvoices.AnyAsync(i => i.Key == scenario.InvoiceKey));
        Assert.Empty(await fresh.SalesInvoiceDivergences.AsNoTracking().ToListAsync());

        var load = await fresh.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == scenario.LoadKey);
        Assert.Equal(0m, load.InvoiceDivergenceQuantity);
        Assert.Equal(0m, load.InvoicedQuantity);
        Assert.Equal(30_000m, load.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, load.Status);
    }
}
