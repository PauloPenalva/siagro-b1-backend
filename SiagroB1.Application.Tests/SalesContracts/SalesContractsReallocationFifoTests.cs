using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Seleção FIFO da liberação de destino e o opt-in explícito de saldo negativo.
///
/// Duas mudanças de contrato em relação ao modo conciliação original:
/// 1. A liberação de destino não é mais escolhida na tela — quando a chave vem nula o
///    servidor consome as liberações ATIVAS do destino em ordem de <c>ReleaseDate</c>.
///    Isso fecha o vazamento em que o ajuste devolvia saldo à liberação de ORIGEM sem
///    consumir nenhuma no destino.
/// 2. Permitir saldo negativo no destino virou flag explícita (<c>allowNegativeBalance</c>),
///    e não mais um efeito colateral da ausência de liberação. É a flag que define a
///    <c>Origin</c>: quem furou o saldo grava Reconciliation, o resto grava Reallocation.
/// </summary>
public class SalesContractsReallocationFifoTests
{
    private const string Reason = "Conciliação com o relatório de entrega do cliente";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsReallocationCreateService Service() =>
        new(_db, new SalesShipmentReleaseMovementGuardService(_db.Context));

    private SalesContractsReallocationDeleteService DeleteService() =>
        new(_db, new TestLogger<SalesContractsReallocationDeleteService>());

    private SalesContract _source = null!;
    private SalesContract _target = null!;
    private SalesInvoiceItem _item = null!;

    /// <summary>
    /// Origem com 500 alocados no formato legado (sem liberação); destino com folga de
    /// contrato suficiente e sem nenhuma liberação — os testes adicionam as que precisam.
    /// </summary>
    private async Task SeedAsync(decimal targetTotalVolume = 1000m)
    {
        _source = NewContract(totalVolume: 1000m, price: 100m);
        _target = NewContract(totalVolume: targetTotalVolume, price: 120m);
        _db.Context.AddRange(_source, _target);

        var invoice = NewInvoice();
        _item = NewItem(invoice, _source.Key, null, quantity: 500m, unitPrice: 90m);
        _db.Context.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(
            _source.Key, _item.Key!.Value, 500m, releaseKey: null,
            invoiceUnitPrice: 90m, contractPrice: 100m));

        _source.AllocatedVolume = 500m;
        await _db.Context.SaveChangesAsync();
    }

    private SalesShipmentRelease AddRelease(decimal released, DateTime releaseDate,
        decimal shipped = 0m, ReleaseStatus status = ReleaseStatus.Actived)
    {
        var release = NewRelease(_target.Key, released, shipped, status);
        release.ReleaseDate = releaseDate;
        _db.Context.Add(release);
        return release;
    }

    private async Task<List<SalesContractAllocation>> TargetLinesAsync() =>
        await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.SalesContractKey == _target.Key)
            .OrderBy(a => a.RowId)
            .ToListAsync();

    [Fact]
    public async Task Fifo_ConsumesOldestReleaseFirst()
    {
        await SeedAsync();
        var newer = AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 10));
        var older = AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 1));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            targetSalesShipmentReleaseKey: null, volume: 300m, userName: "tester");

        var lines = await TargetLinesAsync();
        var line = Assert.Single(lines);
        Assert.Equal(older.Key, line.SalesShipmentReleaseKey);
        Assert.Equal(300m, line.Volume);

        Assert.Equal(300m, (await ReleaseAsync(_db, older.Key)).ShippedQuantity);
        Assert.Equal(0m, (await ReleaseAsync(_db, newer.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Fifo_SpillsIntoNextRelease_KeepingOneGroup()
    {
        await SeedAsync();
        var first = AddRelease(released: 200m, releaseDate: new DateTime(2026, 6, 1));
        var second = AddRelease(released: 300m, releaseDate: new DateTime(2026, 6, 5));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 450m, "tester");

        var lines = await TargetLinesAsync();
        Assert.Equal(2, lines.Count);
        Assert.Single(lines.Select(l => l.ReallocationGroupKey).Distinct());

        // A mais antiga é preenchida até o teto antes de sobrar para a mais nova. Se o FIFO
        // invertesse, a de 2026-06-05 levaria 300 e a de 06-01 apenas 150.
        Assert.Equal(200m, lines.Single(l => l.SalesShipmentReleaseKey == first.Key).Volume);
        Assert.Equal(250m, lines.Single(l => l.SalesShipmentReleaseKey == second.Key).Volume);

        Assert.Equal(200m, (await ReleaseAsync(_db, first.Key)).ShippedQuantity);
        Assert.Equal(250m, (await ReleaseAsync(_db, second.Key)).ShippedQuantity);
        Assert.Equal(450m, (await ContractAsync(_db, _target.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Fifo_ParksRemainderOnNullRelease_WhenReleasesCannotCover()
    {
        await SeedAsync();
        var only = AddRelease(released: 100m, releaseDate: new DateTime(2026, 6, 1));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 400m, "tester");

        var lines = await TargetLinesAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(100m, lines.Single(l => l.SalesShipmentReleaseKey == only.Key).Volume);
        // O que a liberação não absorve fica no formato das linhas legadas.
        Assert.Equal(300m, lines.Single(l => l.SalesShipmentReleaseKey == null).Volume);

        Assert.Equal(100m, (await ReleaseAsync(_db, only.Key)).ShippedQuantity);
        Assert.Equal(400m, (await ContractAsync(_db, _target.Key)).AllocatedVolume);
    }

    /// <summary>
    /// Contrato legado, sem liberação nenhuma, precisa continuar sendo destino válido —
    /// é o caso que motivou a feature inteira.
    /// </summary>
    [Fact]
    public async Task Fifo_TargetWithoutAnyRelease_ProducesSingleNullLine()
    {
        await SeedAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 300m, "tester");

        var line = Assert.Single(await TargetLinesAsync());
        Assert.Null(line.SalesShipmentReleaseKey);
        Assert.Equal(300m, line.Volume);
    }

    [Theory]
    [InlineData(ReleaseStatus.Pending)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    public async Task Fifo_IgnoresNonActiveReleases(ReleaseStatus status)
    {
        await SeedAsync();
        var ignored = AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 1), status: status);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 300m, "tester");

        var line = Assert.Single(await TargetLinesAsync());
        Assert.Null(line.SalesShipmentReleaseKey);
        Assert.Equal(0m, (await ReleaseAsync(_db, ignored.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Fifo_IgnoresExhaustedReleases()
    {
        await SeedAsync();
        var exhausted = AddRelease(released: 200m, releaseDate: new DateTime(2026, 6, 1), shipped: 200m);
        var usable = AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 5));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 300m, "tester");

        var line = Assert.Single(await TargetLinesAsync());
        Assert.Equal(usable.Key, line.SalesShipmentReleaseKey);
        Assert.Equal(200m, (await ReleaseAsync(_db, exhausted.Key)).ShippedQuantity);
    }

    /// <summary>
    /// O guard de saldo passa a depender da FLAG, não da liberação: mesmo com liberação
    /// sobrando, estourar o saldo do contrato sem opt-in é recusado.
    /// </summary>
    [Fact]
    public async Task WithoutOptIn_ExceedingContractBalance_Throws_EvenWithReleaseAvailable()
    {
        await SeedAsync(targetTotalVolume: 200m);
        AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 1));
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, null, 300m, "tester"));
        Assert.Contains("contrato de destino insuficiente", ex.Message);

        Assert.Empty(await TargetLinesAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithOptIn_WithoutReason_Throws(string? reason)
    {
        await SeedAsync(targetTotalVolume: 200m);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, null, 300m, "tester",
            reconciliationReason: reason, allowNegativeBalance: true));
        Assert.Contains("motivo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithOptIn_AllowsNegativeBalance_AndMarksOriginReconciliation()
    {
        await SeedAsync(targetTotalVolume: 200m);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 300m, "tester", reconciliationReason: Reason, allowNegativeBalance: true);

        var line = Assert.Single(await TargetLinesAsync());
        Assert.Equal(SalesContractAllocationOrigin.Reconciliation, line.Origin);
        Assert.Equal(Reason, line.ReconciliationReason);
        Assert.Equal(-100m, (await ContractAsync(_db, _target.Key)).AvaiableVolume);
    }

    /// <summary>
    /// Ajuste que respeitou o saldo grava Reallocation mesmo vindo da tela de conciliação:
    /// a Origin marca quem furou a invariante, não qual tela foi usada.
    /// </summary>
    [Fact]
    public async Task WithoutOptIn_MarksOriginReallocation()
    {
        await SeedAsync();
        AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 1));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 300m, "tester");

        var line = Assert.Single(await TargetLinesAsync());
        Assert.Equal(SalesContractAllocationOrigin.Reallocation, line.Origin);
        Assert.Null(line.ReconciliationReason);
    }

    /// <summary>
    /// O vazamento que motivou o FIFO: conciliar entrega que TINHA liberação de origem
    /// devolvia saldo a ela sem consumir nada no destino. Agora as duas pontas mexem.
    /// </summary>
    [Fact]
    public async Task Fifo_ClosesTheReleaseLeak_SourceGivesBack_AndTargetConsumes()
    {
        _source = NewContract(totalVolume: 1000m, price: 100m);
        _target = NewContract(totalVolume: 1000m, price: 120m);
        var sourceRelease = NewRelease(_source.Key, released: 500m, shipped: 500m);
        _db.Context.AddRange(_source, _target, sourceRelease);

        var invoice = NewInvoice();
        _item = NewItem(invoice, _source.Key, sourceRelease.Key, quantity: 500m, unitPrice: 90m);
        _db.Context.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(
            _source.Key, _item.Key!.Value, 500m, sourceRelease.Key));
        _source.AllocatedVolume = 500m;

        var targetRelease = AddRelease(released: 500m, releaseDate: new DateTime(2026, 6, 1));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 300m, "tester");

        Assert.Equal(200m, (await ReleaseAsync(_db, sourceRelease.Key)).ShippedQuantity);
        Assert.Equal(300m, (await ReleaseAsync(_db, targetRelease.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Reverse_OfMultiReleaseGroup_RestoresEveryRelease()
    {
        await SeedAsync();
        var first = AddRelease(released: 200m, releaseDate: new DateTime(2026, 6, 1));
        var second = AddRelease(released: 300m, releaseDate: new DateTime(2026, 6, 5));
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            null, 450m, "tester");

        var anyLine = (await TargetLinesAsync())[0];
        await DeleteService().ExecuteWithTransactionAsync(anyLine.Key, "tester");

        Assert.Empty(await TargetLinesAsync());
        Assert.Equal(0m, (await ReleaseAsync(_db, first.Key)).ShippedQuantity);
        Assert.Equal(0m, (await ReleaseAsync(_db, second.Key)).ShippedQuantity);
        Assert.Equal(500m, (await ContractAsync(_db, _source.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractAsync(_db, _target.Key)).AllocatedVolume);
    }

    /// <summary>
    /// Chave explícita continua valendo os guards de hoje — é o caminho dos 14 testes de
    /// regressão da realocação operacional.
    /// </summary>
    [Fact]
    public async Task ExplicitRelease_KeepsTodaysGuards()
    {
        await SeedAsync();
        var release = AddRelease(released: 100m, releaseDate: new DateTime(2026, 6, 1));
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            _item.Key!.Value, _source.Key, _target.Key, release.Key, 300m, "tester"));
        Assert.Contains("liberação de destino insuficiente", ex.Message);

        await Service().ExecuteAsync(_item.Key!.Value, _source.Key, _target.Key,
            release.Key, 100m, "tester");

        var line = Assert.Single(await TargetLinesAsync());
        Assert.Equal(release.Key, line.SalesShipmentReleaseKey);
        Assert.Equal(SalesContractAllocationOrigin.Reallocation, line.Origin);
    }
}
