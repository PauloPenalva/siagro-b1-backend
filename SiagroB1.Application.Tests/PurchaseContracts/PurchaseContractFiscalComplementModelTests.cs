using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Complement_is_keyed_by_the_contract()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement
        {
            PurchaseContractKey = contract.Key, UsageCode = 7,
        });
        await _db.Context.SaveChangesAsync();

        var saved = await _db.Context.PurchaseContractFiscalComplements.AsNoTracking().SingleAsync();
        Assert.Equal(contract.Key, saved.PurchaseContractKey);
        Assert.Equal(7, saved.UsageCode);
    }
}
