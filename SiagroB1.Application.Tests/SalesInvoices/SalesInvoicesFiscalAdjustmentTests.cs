using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Efeito do documento de saída AVULSO sobre o contrato de venda: materializado como linha
/// do ledger SALES_CONTRACTS_ALLOCATIONS com origem FiscalAdjustment, aplicado na
/// confirmação e estornado no cancelamento.
/// </summary>
public class SalesInvoicesFiscalAdjustmentTests
{
    private static UsageService Usages(UnitOfWork db) =>
        new(db, NullLogger<UsageService>.Instance);

    private static async Task<UsageModel> SeedUsageAsync(
        UnitOfWork db,
        ContractBalanceEffect balance = ContractBalanceEffect.None,
        ContractValueEffect value = ContractValueEffect.None) =>
        await Usages(db).CreateAsync(new UsageModel
        {
            Name = "Ajuste fiscal",
            CfopOutgoingInState = "5949",
            CfopOutgoingOutState = "6949",
            ContractBalanceEffect = balance,
            ContractValueEffect = value,
            RequiresContract = balance != ContractBalanceEffect.None
                               || value != ContractValueEffect.None,
            RequiresQuantity = false,
        });

    private static SalesContractsAllocationCreateForFiscalAdjustmentService FiscalAdjustment(
        UnitOfWork db) =>
        new(db, new SalesContractsFixedVolumeService(db.Context));

    /// <summary>
    /// Aplica a mesma natureza a todas as linhas — o que o guard produziria para um documento
    /// de natureza única.
    /// </summary>
    private static IReadOnlyList<SalesInvoiceItemUsage> LineUsages(
        SalesInvoice invoice, UsageModel usage) =>
        invoice.Items.Select(i => new SalesInvoiceItemUsage(i, usage)).ToList();

    private static SalesInvoicesConfirmService Confirm(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(Usages(db)),
            FiscalAdjustment(db),
            new FakeStringLocalizer<Resource>());

    private static SalesInvoicesCancelService Cancel(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db),
            NullLogger<SalesInvoicesCancelService>.Instance);

    /// <summary>
    /// Contrato aprovado + documento AVULSO (sem romaneio) de uma linha, já gravados.
    /// </summary>
    private static async Task<(SalesContract Contract, SalesInvoice Invoice)> SeedAsync(
        UnitOfWork db, UsageModel usage,
        decimal quantity = 100m, decimal unitPrice = 90m,
        decimal contractPrice = 100m, decimal allocatedVolume = 0m,
        InvoiceStatus status = InvoiceStatus.Pending)
    {
        var contract = SalesContractsAllocationTestSupport.NewContract(
            totalVolume: 1_000m, price: contractPrice);
        contract.AllocatedVolume = allocatedVolume;

        var invoice = SalesContractsAllocationTestSupport.NewInvoice(status);
        // A natureza é da LINHA.
        var item = SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, releaseKey: null, quantity, unitPrice);
        item.UsageCode = usage.Code;

        db.Context.SalesContracts.Add(contract);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (contract, invoice);
    }

    /// <summary>
    /// Faturamento anterior REAL no ledger. O AllocatedVolume é derivado-da-soma: semear o
    /// número no contrato sem a linha correspondente produziria um saldo que qualquer
    /// recálculo apaga.
    /// </summary>
    private static async Task SeedPriorBillingAsync(
        UnitOfWork db, SalesContract contract, decimal volume)
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice();
        var item = SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, releaseKey: null, volume);

        db.Context.SalesInvoices.Add(invoice);
        db.Context.SalesContractsAllocations.Add(
            SalesContractsAllocationTestSupport.NewAllocation(
                contract.Key, item.Key!.Value, volume));

        var tracked = await db.Context.SalesContracts.SingleAsync(c => c.Key == contract.Key);
        tracked.AllocatedVolume = volume;

        await db.SaveChangesAsync();
    }

    private static async Task<List<SalesContractAllocation>> AllocationsAsync(
        UnitOfWork db, Guid contractKey) =>
        await db.Context.SalesContractsAllocations
            .AsNoTracking()
            .Where(a => a.SalesContractKey == contractKey)
            .ToListAsync();

    [Fact]
    public async Task Consume_writes_a_positive_volume_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, balance: ContractBalanceEffect.Consume);
        var (contract, invoice) = await SeedAsync(db, usage);

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        var allocations = await AllocationsAsync(db, contract.Key);
        var line = Assert.Single(allocations);

        Assert.Equal(100m, line.Volume);
        Assert.Equal(SalesContractAllocationOrigin.FiscalAdjustment, line.Origin);
        Assert.Null(line.SalesShipmentReleaseKey);
        Assert.Equal(100m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }

    [Fact]
    public async Task Restore_writes_a_negative_volume_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, balance: ContractBalanceEffect.Restore);
        var (contract, invoice) = await SeedAsync(db, usage, allocatedVolume: 300m);

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        var line = Assert.Single(await AllocationsAsync(db, contract.Key));

        Assert.Equal(-100m, line.Volume);
        Assert.Equal(-100m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }

    [Fact]
    public async Task None_effects_write_no_ledger_line_at_all()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db);
        var (contract, invoice) = await SeedAsync(db, usage, allocatedVolume: 300m);

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        Assert.Empty(await AllocationsAsync(db, contract.Key));
        Assert.Equal(300m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }

    [Fact]
    public async Task Price_complement_writes_zero_volume_and_a_positive_price_difference()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, value: ContractValueEffect.Add);

        // Complemento: 100 unidades × R$ 10,00 de diferença unitária = R$ 1.000,00.
        var (contract, invoice) = await SeedAsync(db, usage, quantity: 100m, unitPrice: 10m);
        await SeedPriorBillingAsync(db, contract, 100m);

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        var line = Assert.Single(
            (await AllocationsAsync(db, contract.Key))
            .Where(a => a.Origin == SalesContractAllocationOrigin.FiscalAdjustment));

        Assert.Equal(0m, line.Volume);
        Assert.Equal(1_000m, line.PriceDifference);

        // Saldo FÍSICO intacto: a linha de volume zero não mexe no consumo.
        Assert.Equal(100m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }

    [Fact]
    public async Task Price_difference_sum_converges_to_zero_after_the_complement()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, value: ContractValueEffect.Add);
        var (contract, invoice) = await SeedAsync(db, usage, quantity: 100m, unitPrice: 10m);

        // Faturamento anterior: 100 × (90 − 100) = −1.000 apurados (faturou abaixo do contrato).
        var billedItem = SalesContractsAllocationTestSupport.NewItem(
            SalesContractsAllocationTestSupport.NewInvoice(), contract.Key, null, 100m, 90m);
        db.Context.SalesInvoicesItems.Add(billedItem);
        db.Context.SalesContractsAllocations.Add(
            SalesContractsAllocationTestSupport.NewAllocation(
                contract.Key, billedItem.Key!.Value, 100m));
        await db.SaveChangesAsync();

        Assert.Equal(-1_000m,
            (await AllocationsAsync(db, contract.Key)).Sum(a => a.PriceDifference));

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        Assert.Equal(0m, (await AllocationsAsync(db, contract.Key)).Sum(a => a.PriceDifference));
    }

    [Fact]
    public async Task Ledger_invariant_survives_a_zero_volume_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, value: ContractValueEffect.Add);
        var (contract, invoice) = await SeedAsync(db, usage, quantity: 100m, unitPrice: 10m);

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        var itemKey = invoice.Items.Single().Key!.Value;
        var nominal = (await AllocationsAsync(db, contract.Key))
            .Where(a => a.SalesInvoiceItemKey == itemKey)
            .Sum(a => a.Volume);

        // Σ Volume por item = consumo nominal do item, que aqui é zero: a linha de valor
        // não participa do saldo físico.
        Assert.Equal(0m, nominal);
    }

    [Fact]
    public async Task Running_twice_does_not_duplicate_the_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, balance: ContractBalanceEffect.Consume);
        var (contract, invoice) = await SeedAsync(db, usage);

        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");
        await FiscalAdjustment(db).ExecuteAsync(invoice, LineUsages(invoice, usage), "tester");

        Assert.Single(await AllocationsAsync(db, contract.Key));
    }

    [Fact]
    public async Task Confirm_applies_the_effect_of_a_standalone_invoice()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, balance: ContractBalanceEffect.Restore,
            value: ContractValueEffect.Subtract);
        var (contract, invoice) = await SeedAsync(db, usage);
        await SeedPriorBillingAsync(db, contract, 500m);

        await Confirm(db).ExecuteAsync(invoice.Key, "tester");

        var line = Assert.Single(
            (await AllocationsAsync(db, contract.Key))
            .Where(a => a.Origin == SalesContractAllocationOrigin.FiscalAdjustment));

        Assert.Equal(-100m, line.Volume);
        Assert.Equal(-9_000m, line.PriceDifference);
        Assert.Equal(400m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }

    [Fact]
    public async Task Pending_invoice_does_not_move_the_balance()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, balance: ContractBalanceEffect.Consume);
        var (contract, _) = await SeedAsync(db, usage, allocatedVolume: 500m);

        // Documento criado e não confirmado: nada de ledger, nada de saldo.
        Assert.Empty(await AllocationsAsync(db, contract.Key));
        Assert.Equal(500m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }

    [Fact]
    public async Task Cancel_reverses_exactly_what_confirm_applied()
    {
        var db = TestDb.CreateUnitOfWork();
        var usage = await SeedUsageAsync(db, balance: ContractBalanceEffect.Restore,
            value: ContractValueEffect.Subtract);
        var (contract, invoice) = await SeedAsync(db, usage);
        await SeedPriorBillingAsync(db, contract, 500m);

        await Confirm(db).ExecuteAsync(invoice.Key, "tester");
        await Cancel(db).ExecuteAsync(invoice.Key, "tester");

        // Só o faturamento anterior sobrevive: o ajuste fiscal foi removido junto com a nota.
        Assert.DoesNotContain(await AllocationsAsync(db, contract.Key),
            a => a.Origin == SalesContractAllocationOrigin.FiscalAdjustment);
        Assert.Equal(500m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }
}
