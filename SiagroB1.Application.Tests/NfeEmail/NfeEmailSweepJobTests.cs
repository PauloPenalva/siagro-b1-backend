using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.NfeEmail;

public class NfeEmailSweepJobTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeBackgroundJobClient _jobs = new();

    private NfeEmailMessage Seed(NfeEmailStatus status, DateTime createdAt, NfeEmailTrigger trigger = NfeEmailTrigger.Automatic)
    {
        var row = new NfeEmailMessage
        {
            DocumentType = NfeEmailDocumentType.Sales, DocumentKey = Guid.NewGuid(), EventType = NfeEmailEventType.Authorized,
            Trigger = trigger, Status = status, CreatedAt = createdAt, CreatedBy = "tester",
        };
        _db.Context.NfeEmailMessages.Add(row);
        _db.Context.SaveChanges();
        return row;
    }

    [Fact]
    public async Task Enqueues_only_pending_older_than_grace()
    {
        var due = Seed(NfeEmailStatus.Pending, DateTime.Now.AddMinutes(-5));
        Seed(NfeEmailStatus.Pending, DateTime.Now);
        Seed(NfeEmailStatus.Sent, DateTime.Now.AddMinutes(-5));
        Seed(NfeEmailStatus.Failed, DateTime.Now.AddMinutes(-5));
        Seed(NfeEmailStatus.Pending, DateTime.Now.AddMinutes(-5), NfeEmailTrigger.Manual);

        await new NfeEmailSweepJob(_db.Context, _jobs, new FakeCompanyContext("0002"), NullLogger<NfeEmailSweepJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

        Assert.Equal(due.Key, Assert.Single(_jobs.EnqueuedOutboxKeys));
        Assert.Equal(["0002"], _jobs.EnqueuedCompanyCodes);
    }
}
