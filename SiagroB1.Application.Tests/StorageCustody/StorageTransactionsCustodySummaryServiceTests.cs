using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageCustody;

public class StorageTransactionsCustodySummaryServiceTests
{
    [Fact]
    public async Task Lists_the_expected_steps_with_their_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        var summary = await new StorageTransactionsCustodySummaryService(db).ExecuteAsync(t.Key);

        Assert.True(summary.Applies);
        Assert.Equal(3, summary.Steps.Count);
        Assert.Equal(StorageCustodyStepSituation.Generated, summary.Steps[0].Situation);
        Assert.Equal(StorageCustodyRole.ProcessingSymbolicReturn, summary.NextRole);
    }

    [Fact]
    public async Task Own_lot_romaneio_does_not_apply()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        seed.Lot.OwnershipType = StorageOwnershipType.OwnedInOurCustody;
        await db.SaveChangesAsync();
        var t = await seed.AddTransactionAsync();

        var summary = await new StorageTransactionsCustodySummaryService(db).ExecuteAsync(t.Key);

        Assert.False(summary.Applies);
        Assert.Empty(summary.Steps);
    }

    [Fact]
    public async Task Unknown_romaneio_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<NotFoundException>(() => new StorageTransactionsCustodySummaryService(db).ExecuteAsync(Guid.NewGuid()));
    }
}
