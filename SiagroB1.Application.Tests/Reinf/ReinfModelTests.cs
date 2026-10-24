using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfModelTests
{
    [Fact]
    public async Task SavesEventAndBatch()
    {
        var name = Guid.NewGuid().ToString();
        var batchKey = Guid.NewGuid();
        var db = TestDb.CreateUnitOfWork(name);
        db.Context.ReinfBatches.Add(new ReinfBatch
        {
            Key = batchKey, TaxIdRoot = "12345678", Environment = ReinfEnvironment.ProductionRestricted,
            Status = ReinfBatchStatus.Sent, RequestXml = "<x/>", SentAt = DateTime.UtcNow,
        });
        db.Context.ReinfEvents.Add(new ReinfEvent
        {
            Type = ReinfEventType.R2055, TaxIdRoot = "12345678", Period = "2026-09",
            EventId = "ID" + new string('1', 34), RectificationIndicator = 1, Xml = "<e/>", BatchKey = batchKey,
            GrossValue = 100.55m,
        });
        await db.SaveChangesAsync();

        var db2 = TestDb.CreateUnitOfWork(name);
        var e = await db2.Context.ReinfEvents.SingleAsync();
        Assert.Equal(ReinfEventStatus.Pending, e.Status);
        Assert.Equal(36, e.EventId.Length);
        Assert.Equal(batchKey, e.BatchKey);
        Assert.Equal(ReinfBatchStatus.Sent, (await db2.Context.ReinfBatches.SingleAsync()).Status);
    }
}
