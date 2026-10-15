using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15 D7: realocar desmembra o firme solto; desfazer junta de volta; composto recusa.</summary>
public class SalesFirmReceivableReallocationTests
{
    private static SalesContractsReallocationCreateService Create(SalesFirmReceivableSeed seed) =>
        new(seed.Db, new SalesShipmentReleaseMovementGuardService(seed.Context), salesFirmSync: seed.Sync());

    private static SalesContractsReallocationDeleteService Delete(SalesFirmReceivableSeed seed) =>
        new(seed.Db, new TestLogger<SalesContractsReallocationDeleteService>(), salesFirmSync: seed.Sync());

    [Fact]
    public async Task Reallocating_part_of_the_line_splits_the_firm_and_undoing_joins_it_back()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.ConfirmedAsync();

        await Create(seed).ExecuteWithTransactionAsync(line.Key!.Value, seed.Contract.Key, other.Key, null, 400m, "ana");

        var titles = await seed.TitlesOfAsync(line);
        Assert.Equal(54_000m, titles.Single(t => t.SalesContractKey == seed.Contract.Key).NetAmount);
        Assert.Equal(36_000m, titles.Single(t => t.SalesContractKey == other.Key).NetAmount);

        var pairLine = await seed.Context.SalesContractsAllocations.AsNoTracking()
            .FirstAsync(a => a.Origin == SalesContractAllocationOrigin.Reallocation);
        await Delete(seed).ExecuteWithTransactionAsync(pairLine.Key, "ana");

        var after = await seed.TitlesOfAsync(line);
        Assert.Equal(90_000m, Assert.Single(after).NetAmount);
        Assert.Equal(seed.Contract.Key, after.Single().SalesContractKey);
    }

    [Fact]
    public async Task Reallocating_a_composed_firm_is_refused_and_nothing_is_written()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            Create(seed).ExecuteWithTransactionAsync(line.Key!.Value, seed.Contract.Key, other.Key, null, 400m, "ana"));

        Assert.Equal(
            $"O título {firm.Code} da linha realocada já está composto no título FN-PROV-V1: desfaça a composição antes de realocar.",
            error.Message);
        Assert.False(await seed.Context.SalesContractsAllocations.AsNoTracking()
            .AnyAsync(a => a.Origin == SalesContractAllocationOrigin.Reallocation));
        Assert.Single(await seed.TitlesOfAsync(line));
    }
}
