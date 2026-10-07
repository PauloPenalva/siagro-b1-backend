using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractFiscalComplementModelTests
{
    [Fact]
    public async Task Complement_is_saved_by_contract_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var key = Guid.NewGuid();
        db.Context.SalesContractFiscalComplements.Add(new SalesContractFiscalComplement
        {
            SalesContractKey = key, UsageCode = 3, PaymentConditionCode = 11,
            AdditionalInfo = "Pedido 77", CustomerOrderNumber = "PO-77", CustomerOrderItem = "1",
        });
        await db.SaveChangesAsync();

        var saved = await db.Context.SalesContractFiscalComplements.AsNoTracking().SingleAsync(x => x.SalesContractKey == key);
        Assert.Equal(3, saved.UsageCode);
        Assert.Equal("PO-77", saved.CustomerOrderNumber);
    }
}
