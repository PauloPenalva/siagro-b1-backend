using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Consultas que alimentam a tela de conciliação. O invariante que estes testes protegem
/// é o oposto do resto do sistema: aqui contrato SEM saldo e SEM liberação PRECISA
/// aparecer — filtrá-lo é o que deixa a conciliação sem saída.
/// </summary>
public class SalesContractsReconciliationQueriesTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Targets_IncludeExhaustedAndNegativeContracts_WithoutRequiringRelease()
    {
        var source = NewContract(totalVolume: 1000m);
        var exhausted = NewContract(totalVolume: 500m);
        var negative = NewContract(totalVolume: 500m);
        exhausted.AllocatedVolume = 500m;  // saldo 0
        negative.AllocatedVolume = 800m;   // saldo −300

        var invoice = NewInvoice();
        var item = NewItem(invoice, source.Key, null, quantity: 200m);
        _db.Context.AddRange(source, exhausted, negative, invoice);
        await _db.Context.SaveChangesAsync();

        var targets = await new SalesContractsGetReconciliationTargetsService(_db)
            .ExecuteAsync(item.Key!.Value, source.Key);

        var keys = targets.Select(t => t.SalesContractKey).ToList();
        Assert.Contains(exhausted.Key.ToString(), keys);
        Assert.Contains(negative.Key.ToString(), keys);
        Assert.DoesNotContain(source.Key.ToString(), keys); // a origem nunca é destino

        Assert.Equal(0m, targets.Single(t => t.SalesContractKey == exhausted.Key.ToString()).Balance);
        Assert.Equal(-300m, targets.Single(t => t.SalesContractKey == negative.Key.ToString()).Balance);
    }

    [Fact]
    public async Task Targets_ExcludeFinishedAndMismatchedContracts()
    {
        var source = NewContract(totalVolume: 1000m);
        var finished = NewContract(totalVolume: 500m, status: ContractStatus.Finished);
        var otherCustomer = NewContract(totalVolume: 500m, cardCode: "C9999");
        var otherItem = NewContract(totalVolume: 500m, itemCode: "MILHO");
        var otherUom = NewContract(totalVolume: 500m, uom: "SC");
        var valid = NewContract(totalVolume: 500m);

        var invoice = NewInvoice();
        var item = NewItem(invoice, source.Key, null, quantity: 200m);
        _db.Context.AddRange(source, finished, otherCustomer, otherItem, otherUom, valid, invoice);
        await _db.Context.SaveChangesAsync();

        var targets = await new SalesContractsGetReconciliationTargetsService(_db)
            .ExecuteAsync(item.Key!.Value, source.Key);

        Assert.Equal([valid.Key.ToString()], targets.Select(t => t.SalesContractKey).ToList());
    }

    [Fact]
    public async Task NegativeBalances_ReturnsOnlyNegatives_ExcludingFinished()
    {
        var positive = NewContract(totalVolume: 1000m);
        var zero = NewContract(totalVolume: 500m);
        var negative = NewContract(totalVolume: 500m);
        var finishedNegative = NewContract(totalVolume: 500m, status: ContractStatus.Finished);
        positive.AllocatedVolume = 200m;
        zero.AllocatedVolume = 500m;
        negative.AllocatedVolume = 800m;
        finishedNegative.AllocatedVolume = 900m;

        _db.Context.AddRange(positive, zero, negative, finishedNegative);
        await _db.Context.SaveChangesAsync();

        var rows = await new SalesContractsGetNegativeBalancesService(_db).ExecuteAsync();

        Assert.Equal([negative.Key.ToString()], rows.Select(r => r.SalesContractKey).ToList());
        Assert.Equal(-300m, rows.Single().Balance);
    }
}
