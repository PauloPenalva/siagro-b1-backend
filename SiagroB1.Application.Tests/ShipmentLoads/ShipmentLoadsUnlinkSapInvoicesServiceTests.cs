using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>"Desvincular NFs do SAP" (GAC-1226): cancela todos os documentos vinculados da carga só Faturada.</summary>
public class ShipmentLoadsUnlinkSapInvoicesServiceTests
{
    private static IConfiguration SapMode() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "SAPB1" }).Build();

    private static (ShipmentLoadsUnlinkSapInvoicesService Service, CountingUnitOfWork Db) Build(string name)
    {
        var counting = new CountingUnitOfWork(TestDb.CreateUnitOfWork(name));
        var cancel = new SalesInvoicesCancelService(counting,
            new SalesShipmentReleasesRecalculateShippedService(counting.Context),
            new SalesContractsAllocationDeleteForInvoiceService(counting),
            new ShipmentLoadsBalanceHookService(counting.Context, new ShipmentLoadsMovementLogService(counting.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

        return (new ShipmentLoadsUnlinkSapInvoicesService(counting, cancel, SapMode(),
            NullLogger<ShipmentLoadsUnlinkSapInvoicesService>.Instance), counting);
    }

    private static async Task<(SalesInvoiceDivergenceScenario Scenario, string DbName)> SeedAsync()
    {
        var name = Guid.NewGuid().ToString();
        await using var seed = new AppDbContext(TestDb.Options(name));
        return (await SalesInvoiceDivergenceTestSeed.SeedAsync(seed), name);
    }

    /// <summary>Segundo documento da mesma carga (sem divergência), vinculado a outra NF do SAP.</summary>
    private static async Task<Guid> AddSecondSapDocumentAsync(string name, Guid loadKey, int sapDocEntry = 2)
    {
        await using var context = new AppDbContext(TestDb.Options(name));
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            BranchCode = "3",
            InvoiceNumber = $"DS{sapDocEntry:D6}",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = loadKey,
            SapDocEntry = sapDocEntry,
            SapDocNum = sapDocEntry + 1000,
            SapLinkType = SapLinkType.Normal,
        };
        context.SalesInvoices.Add(invoice);
        context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", Quantity = 100m, UnitPrice = 2m,
        });
        await context.SaveChangesAsync();

        return invoice.Key;
    }

    private static async Task SetLoadStatusAsync(string name, Guid loadKey, ShipmentLoadStatus status)
    {
        await using var context = new AppDbContext(TestDb.Options(name));
        (await context.ShipmentLoads.SingleAsync(l => l.Key == loadKey)).Status = status;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Unlinks_every_sap_document_of_an_invoiced_load()
    {
        var (scenario, name) = await SeedAsync();
        var secondKey = await AddSecondSapDocumentAsync(name, scenario.LoadKey);
        var (service, db) = Build(name);

        var count = await service.ExecuteAsync(scenario.LoadKey, "tester");

        Assert.Equal(2, count);
        Assert.Equal((1, 1, 0), (db.Begins, db.Commits, db.Rollbacks));

        await using var fresh = new AppDbContext(TestDb.Options(name));
        var invoices = await fresh.SalesInvoices.AsNoTracking().ToListAsync();
        Assert.All(invoices, i => Assert.Equal(InvoiceStatus.Cancelled, i.InvoiceStatus));
        Assert.Contains(invoices, i => i.Key == scenario.InvoiceKey);
        Assert.Contains(invoices, i => i.Key == secondKey);

        var divergence = await fresh.SalesInvoiceDivergences.AsNoTracking().SingleAsync();
        Assert.Equal(SalesInvoiceDivergenceStatus.Voided, divergence.Status);
        Assert.Equal(SalesInvoiceDivergenceRules.UnlinkedReason, divergence.VoidReason);

        var load = await fresh.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == scenario.LoadKey);
        Assert.Equal(ShipmentLoadStatus.Open, load.Status);
        Assert.Equal(load.TotalQuantity, load.AvailableQuantity);
        Assert.Equal(0m, load.InvoiceDivergenceQuantity);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Discharged)]
    [InlineData(ShipmentLoadStatus.Completed)]
    public async Task Discharged_or_completed_load_is_refused(ShipmentLoadStatus status)
    {
        var (scenario, name) = await SeedAsync();
        await SetLoadStatusAsync(name, scenario.LoadKey, status);
        var (service, db) = Build(name);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Equal("Desfaça a descarga e a conferência antes de desvincular as notas fiscais.", ex.Message);
        Assert.Equal(0, db.Begins);
    }

    [Fact]
    public async Task Open_load_is_refused()
    {
        var (scenario, name) = await SeedAsync();
        await SetLoadStatusAsync(name, scenario.LoadKey, ShipmentLoadStatus.Open);
        var (service, _) = Build(name);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Equal("A carga CG000010 não está faturada: não há notas fiscais do SAP para desvincular.", ex.Message);
    }

    [Fact]
    public async Task Load_with_a_refusal_return_is_refused()
    {
        var (scenario, name) = await SeedAsync();
        await using (var context = new AppDbContext(TestDb.Options(name)))
        {
            context.SalesInvoices.Add(new SalesInvoice
            {
                Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", InvoiceNumber = "DV000001",
                InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Return,
                SalesInvoiceOriginKey = scenario.InvoiceKey, ShipmentLoadKey = scenario.LoadKey,
            });
            await context.SaveChangesAsync();
        }

        var (service, db) = Build(name);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Equal("Estorne a recusa antes de desvincular.", ex.Message);
        Assert.Equal(0, db.Begins);
    }

    [Fact]
    public async Task Load_without_sap_documents_is_refused()
    {
        var (scenario, name) = await SeedAsync();
        await using (var context = new AppDbContext(TestDb.Options(name)))
        {
            (await context.SalesInvoices.SingleAsync()).SapDocEntry = null;
            await context.SaveChangesAsync();
        }

        var (service, _) = Build(name);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Equal("A carga CG000010 não tem nota fiscal do SAP vinculada.", ex.Message);
    }

    [Fact]
    public async Task Pending_document_is_refused()
    {
        var (scenario, name) = await SeedAsync();
        await using (var context = new AppDbContext(TestDb.Options(name)))
        {
            (await context.SalesInvoices.SingleAsync()).InvoiceStatus = InvoiceStatus.Pending;
            await context.SaveChangesAsync();
        }

        var (service, _) = Build(name);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Equal("O documento DS000001 está pendente. Confirme-o ou exclua-o antes de desvincular.", ex.Message);
    }

    /// <summary>
    /// O EF InMemory não desfaz gravações: aqui só se prova o que dá — a mensagem da trava sobe e a transação é
    /// desfeita (Rollback uma vez, nenhum Commit). Que o estado volta atrás é garantia do SQL Server.
    /// </summary>
    [Fact]
    public async Task A_cancel_lock_on_one_document_rolls_back_all()
    {
        var (scenario, name) = await SeedAsync();
        var secondKey = await AddSecondSapDocumentAsync(name, scenario.LoadKey);
        await using (var context = new AppDbContext(TestDb.Options(name)))
        {
            context.FinancialDocuments.Add(new FinancialDocument
            {
                Key = Guid.NewGuid(), Code = "FN000009", CardCode = "C0001",
                Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.PartiallySettled, DueDate = DateTime.Today,
                NetAmount = 100m, SettledAmount = 10m, SalesInvoiceKey = secondKey,
                OriginType = FinancialDocumentOrigin.SalesInvoice,
            });
            await context.SaveChangesAsync();
        }

        var (service, db) = Build(name);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Contains("possui baixa financeira", ex.Message);
        Assert.Contains("DS000002", ex.Message);
        Assert.Equal((1, 0, 1), (db.Begins, db.Commits, db.Rollbacks));
    }

    [Fact]
    public async Task Manual_documents_of_the_load_are_left_alone()
    {
        var (scenario, name) = await SeedAsync();
        Guid manualKey;
        await using (var context = new AppDbContext(TestDb.Options(name)))
        {
            manualKey = Guid.NewGuid();
            context.SalesInvoices.Add(new SalesInvoice
            {
                Key = manualKey, CardCode = "C0001", BranchCode = "3", InvoiceNumber = "DS900001",
                InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal,
                ShipmentLoadKey = scenario.LoadKey,
            });
            await context.SaveChangesAsync();
        }

        var (service, _) = Build(name);

        var count = await service.ExecuteAsync(scenario.LoadKey, "tester");

        Assert.Equal(1, count);
        await using var fresh = new AppDbContext(TestDb.Options(name));
        Assert.Equal(InvoiceStatus.Confirmed,
            (await fresh.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == manualKey)).InvoiceStatus);
        Assert.Equal(InvoiceStatus.Cancelled,
            (await fresh.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == scenario.InvoiceKey)).InvoiceStatus);
    }

    [Fact]
    public async Task Refuses_outside_sap_mode()
    {
        var (scenario, name) = await SeedAsync();
        var counting = new CountingUnitOfWork(TestDb.CreateUnitOfWork(name));
        var service = new ShipmentLoadsUnlinkSapInvoicesService(counting, null!, new ConfigurationBuilder().Build());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(scenario.LoadKey, "tester"));

        Assert.Equal("Disponível somente com integração SAP Business One.", ex.Message);
    }
}
