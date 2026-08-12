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
/// Modo CONCILIAÇÃO da realocação de venda: com <c>allowNegativeBalance</c>, o saldo do
/// contrato de destino pode ficar negativo — em troca de um motivo obrigatório. É o
/// caminho que destrava os contratos legados cujo saldo ficou negativo, em especial a
/// TROCA CRUZADA, em que cada movimento dependeria do outro ter acontecido antes.
///
/// ⚠️ A permissão vem da FLAG, não da ausência de liberação de destino: passar
/// <c>TargetSalesShipmentReleaseKey = null</c> apenas delega a escolha da liberação ao
/// FIFO do servidor (ver <see cref="SalesContractsReallocationFifoTests"/>).
/// </summary>
public class SalesContractsReconciliationTests
{
    private const string Reason = "Conciliação com o relatório de entrega do cliente";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsReallocationCreateService Service() =>
        new(_db, new SalesShipmentReleaseMovementGuardService(_db.Context));

    private SalesContractsReallocationDeleteService DeleteService() =>
        new(_db, new TestLogger<SalesContractsReallocationDeleteService>());

    /// <summary>
    /// Cria uma nota confirmada Normal com um único item já alocado no contrato, no
    /// formato legado: sem liberação de entrega (<c>SalesShipmentReleaseKey = null</c>).
    /// </summary>
    private SalesInvoiceItem SeedLegacyDelivery(SalesContract contract, decimal volume, decimal unitPrice = 90m)
    {
        var invoice = NewInvoice();
        var item = NewItem(invoice, contract.Key, null, quantity: volume, unitPrice: unitPrice);
        _db.Context.Add(invoice);
        _db.Context.SalesContractsAllocations.Add(NewAllocation(
            contract.Key, item.Key!.Value, volume, releaseKey: null,
            invoiceUnitPrice: unitPrice, contractPrice: contract.Price));
        return item;
    }

    [Fact]
    public async Task Reconcile_TargetWithoutReleaseAndWithoutBalance_Succeeds_AndTargetGoesNegative()
    {
        // Origem já negativa: 1.200 alocados para 1.000 de contrato → saldo −200.
        var source = NewContract(totalVolume: 1000m, price: 100m);
        var target = NewContract(totalVolume: 500m, price: 120m);
        _db.Context.AddRange(source, target);

        var item = SeedLegacyDelivery(source, 200m);
        SeedLegacyDelivery(source, 1000m);
        // Destino esgotado (saldo exatamente 0) e SEM nenhuma liberação de entrega.
        SeedLegacyDelivery(target, 500m);
        source.AllocatedVolume = 1200m;
        target.AllocatedVolume = 500m;
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(item.Key!.Value, source.Key, target.Key,
            targetSalesShipmentReleaseKey: null, volume: 200m, userName: "tester",
            reconciliationReason: Reason, allowNegativeBalance: true);

        var rows = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.Origin == SalesContractAllocationOrigin.Reconciliation)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].ReallocationGroupKey, rows[1].ReallocationGroupKey);
        Assert.All(rows, r => Assert.Equal(Reason, r.ReconciliationReason));

        var negative = rows.Single(r => r.Volume < 0);
        Assert.Equal(-200m, negative.Volume);
        Assert.Equal(source.Key, negative.SalesContractKey);
        Assert.Null(negative.SalesShipmentReleaseKey);
        Assert.Equal(target.Key, negative.CounterpartySalesContractKey);

        var positive = rows.Single(r => r.Volume > 0);
        Assert.Equal(200m, positive.Volume);
        Assert.Equal(target.Key, positive.SalesContractKey);
        // O destino não tem liberação: a linha nasce no mesmo formato das linhas legadas.
        Assert.Null(positive.SalesShipmentReleaseKey);
        Assert.Equal(120m, positive.ContractPrice);
        Assert.Equal(source.Key, positive.CounterpartySalesContractKey);

        // Origem cicatriza (−200 → 0) e o destino assume o negativo, como decidido.
        var reloadedSource = await ContractAsync(_db, source.Key);
        var reloadedTarget = await ContractAsync(_db, target.Key);
        Assert.Equal(1000m, reloadedSource.AllocatedVolume);
        Assert.Equal(0m, reloadedSource.AvaiableVolume);
        Assert.Equal(700m, reloadedTarget.AllocatedVolume);
        Assert.Equal(-200m, reloadedTarget.AvaiableVolume);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reconcile_WithoutReason_Throws(string? reason)
    {
        var source = NewContract(totalVolume: 1000m);
        var target = NewContract(totalVolume: 1000m);
        _db.Context.AddRange(source, target);
        var item = SeedLegacyDelivery(source, 200m);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, null, 200m, "tester", reason,
            allowNegativeBalance: true));
        Assert.Contains("motivo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// O caso que hoje é IMPOSSÍVEL: a nota que está em A pertence ao B e a que está em B
    /// pertence ao A, com os dois contratos zerados. Pela realocação normal, cada movimento
    /// exigiria que o outro já tivesse acontecido (o destino precisa de liberação ativa e
    /// de saldo positivo) — um ciclo. Em conciliação, os dois passam.
    /// </summary>
    [Fact]
    public async Task Reconcile_CrossedSwapBetweenExhaustedContracts_Succeeds()
    {
        var contractA = NewContract(totalVolume: 500m, price: 100m);
        var contractB = NewContract(totalVolume: 500m, price: 110m);
        _db.Context.AddRange(contractA, contractB);

        // Cada nota está no contrato errado; ambos os contratos ficam com saldo 0.
        var belongsToB = SeedLegacyDelivery(contractA, 500m);
        var belongsToA = SeedLegacyDelivery(contractB, 500m);
        contractA.AllocatedVolume = 500m;
        contractB.AllocatedVolume = 500m;
        await _db.Context.SaveChangesAsync();

        // Passo 1: A → B. O destino fica temporariamente em −500.
        await Service().ExecuteAsync(belongsToB.Key!.Value, contractA.Key, contractB.Key,
            null, 500m, "tester", Reason, allowNegativeBalance: true);
        Assert.Equal(-500m, (await ContractAsync(_db, contractB.Key)).AvaiableVolume);
        Assert.Equal(500m, (await ContractAsync(_db, contractA.Key)).AvaiableVolume);

        // Passo 2: B → A. Fecha o ciclo.
        await Service().ExecuteAsync(belongsToA.Key!.Value, contractB.Key, contractA.Key,
            null, 500m, "tester", Reason, allowNegativeBalance: true);

        var finalA = await ContractAsync(_db, contractA.Key);
        var finalB = await ContractAsync(_db, contractB.Key);
        Assert.Equal(0m, finalA.AvaiableVolume);
        Assert.Equal(0m, finalB.AvaiableVolume);

        // E cada nota terminou no contrato certo.
        Assert.Equal(500m, await NetVolumeAsync(contractB.Key, belongsToB.Key!.Value));
        Assert.Equal(0m, await NetVolumeAsync(contractA.Key, belongsToB.Key!.Value));
        Assert.Equal(500m, await NetVolumeAsync(contractA.Key, belongsToA.Key!.Value));
        Assert.Equal(0m, await NetVolumeAsync(contractB.Key, belongsToA.Key!.Value));
    }

    [Fact]
    public async Task Reconcile_PreservesSharedGuards()
    {
        var source = NewContract(totalVolume: 1000m);
        var target = NewContract(totalVolume: 1000m);
        _db.Context.AddRange(source, target);
        var item = SeedLegacyDelivery(source, 200m);
        await _db.Context.SaveChangesAsync();

        // Volume acima do saldo alocado da nota na origem continua bloqueado.
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, null, 201m, "tester", Reason));
        Assert.Contains("superior ao saldo alocado", ex.Message);

        // Volume inválido e mesmo contrato continuam bloqueados.
        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, null, 0m, "tester", Reason));
        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, source.Key, null, 200m, "tester", Reason));

        // Produto diferente continua bloqueado. (Cliente diferente NÃO bloqueia mais —
        // ver Execute_DifferentCustomer_CreatesPair.)
        var trackedTarget = await _db.Context.SalesContracts.SingleAsync(c => c.Key == target.Key);
        trackedTarget.ItemCode = "MILHO";
        await _db.Context.SaveChangesAsync();
        ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, null, 200m, "tester", Reason));
        Assert.Contains("outro produto", ex.Message);

        // Contrato encerrado continua bloqueado.
        trackedTarget.ItemCode = "SOJA";
        trackedTarget.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();
        ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, null, 200m, "tester", Reason));
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task Reconcile_NotConfirmedInvoice_Throws()
    {
        var source = NewContract(totalVolume: 1000m);
        var target = NewContract(totalVolume: 1000m);
        _db.Context.AddRange(source, target);
        var item = SeedLegacyDelivery(source, 200m);
        await _db.Context.SaveChangesAsync();

        var invoice = await _db.Context.SalesInvoices.SingleAsync(i => i.Key == item.SalesInvoiceKey);
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, null, 200m, "tester", Reason));
        Assert.Contains("Somente notas confirmadas", ex.Message);
    }

    [Fact]
    public async Task Reconcile_SelfHealsCorruptedBalances()
    {
        var source = NewContract(totalVolume: 1000m);
        var target = NewContract(totalVolume: 1000m);
        _db.Context.AddRange(source, target);
        var item = SeedLegacyDelivery(source, 200m);
        source.AllocatedVolume = 999m; // corrompido
        target.AllocatedVolume = 777m; // corrompido
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(item.Key!.Value, source.Key, target.Key,
            null, 200m, "tester", Reason);

        Assert.Equal(0m, (await ContractAsync(_db, source.Key)).AllocatedVolume);
        Assert.Equal(200m, (await ContractAsync(_db, target.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task Reverse_OfReconciliation_RestoresBalances()
    {
        var source = NewContract(totalVolume: 1000m);
        var target = NewContract(totalVolume: 1000m);
        _db.Context.AddRange(source, target);
        var item = SeedLegacyDelivery(source, 200m);
        source.AllocatedVolume = 200m;
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(item.Key!.Value, source.Key, target.Key,
            null, 200m, "tester", Reason, allowNegativeBalance: true);

        var reconciliationRow = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .FirstAsync(a => a.Origin == SalesContractAllocationOrigin.Reconciliation && a.Volume > 0);

        await DeleteService().ExecuteWithTransactionAsync(reconciliationRow.Key, "tester");

        Assert.Empty(await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.Origin == SalesContractAllocationOrigin.Reconciliation)
            .ToListAsync());
        Assert.Equal(200m, (await ContractAsync(_db, source.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ContractAsync(_db, target.Key)).AllocatedVolume);
    }

    /// <summary>
    /// Com liberação informada, nada muda: os guards operacionais continuam valendo mesmo
    /// que um motivo seja enviado junto.
    /// </summary>
    [Fact]
    public async Task WithRelease_KeepsOperationalGuards_EvenWhenReasonIsSupplied()
    {
        var source = NewContract(totalVolume: 1000m);
        var target = NewContract(totalVolume: 1000m);
        var targetRelease = NewRelease(target.Key, released: 100m);
        _db.Context.AddRange(source, target, targetRelease);
        var item = SeedLegacyDelivery(source, 200m);
        source.AllocatedVolume = 200m;
        await _db.Context.SaveChangesAsync();

        // Saldo da liberação de destino (100) menor que o volume (200) segue bloqueando.
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            item.Key!.Value, source.Key, target.Key, targetRelease.Key, 200m, "tester", Reason));
        Assert.Contains("liberação de destino insuficiente", ex.Message);

        // E o caminho feliz continua nascendo como Realocação, não como Conciliação.
        await Service().ExecuteAsync(item.Key!.Value, source.Key, target.Key,
            targetRelease.Key, 100m, "tester", Reason);

        var positive = await _db.Context.SalesContractsAllocations.AsNoTracking()
            .FirstAsync(a => a.SalesContractKey == target.Key && a.Volume > 0);
        Assert.Equal(SalesContractAllocationOrigin.Reallocation, positive.Origin);
        Assert.Equal(targetRelease.Key, positive.SalesShipmentReleaseKey);
    }

    private async Task<decimal> NetVolumeAsync(Guid contractKey, Guid itemKey) =>
        await _db.Context.SalesContractsAllocations.AsNoTracking()
            .Where(a => a.SalesContractKey == contractKey && a.SalesInvoiceItemKey == itemKey)
            .SumAsync(a => a.Volume);
}
