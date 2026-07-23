using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Regressão da decisão "a fixação vira a fonte única do preço": o snapshot ContractPrice
/// do ledger de alocações vem da fixação Confirmed vigente, não de <c>SalesContract.Price</c>.
/// </summary>
public class SalesContractsAllocationFixationPriceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsAllocationCreateService Service() =>
        new(_db, new SalesContractsFixedVolumeService(_db.Context));

    [Fact]
    public async Task Billing_Paf_SnapshotsConfirmedFixationPrice_NotZeroContractPrice()
    {
        // PAF: Price = 0 (o preço vive nas fixações). Fixação confirmada a 2,50.
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = Guid.NewGuid().ToString("N")[..8],
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 1000m,
            Price = 0m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            FixationVolume = 1000m,
            FixationPrice = 2.5m,
            Status = PriceFixationStatus.Confirmed,
        });

        var invoice = NewInvoice();
        NewItem(invoice, contract.Key, null, quantity: 200m, unitPrice: 90m);
        _db.Context.Add(invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteForInvoiceAsync(invoice, "tester");

        var allocation = await _db.Context.SalesContractsAllocations.AsNoTracking().SingleAsync();
        Assert.Equal(2.5m, allocation.ContractPrice);
        // 200 × (90 − 2,50) = 17.500
        Assert.Equal(17_500m, allocation.PriceDifference);
    }

    [Fact]
    public async Task Billing_NoConfirmedFixation_FallsBackToContractPrice()
    {
        // Contrato de preço fixo (seed sem fixação): fallback para Price mantém o
        // comportamento anterior do ledger — nenhuma regressão para contratos fixos.
        var contract = NewContract(totalVolume: 1000m, price: 100m);
        var invoice = NewInvoice();
        NewItem(invoice, contract.Key, null, quantity: 200m, unitPrice: 90m);
        _db.Context.AddRange(contract, invoice);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteForInvoiceAsync(invoice, "tester");

        var allocation = await _db.Context.SalesContractsAllocations.AsNoTracking().SingleAsync();
        Assert.Equal(100m, allocation.ContractPrice);
        Assert.Equal(-2000m, allocation.PriceDifference);
    }
}
