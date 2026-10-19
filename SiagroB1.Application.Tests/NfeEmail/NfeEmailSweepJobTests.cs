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

    private NfeEmailMessage Seed(NfeEmailStatus status, DateTime createdAt, NfeEmailTrigger trigger = NfeEmailTrigger.Automatic,
        int attempts = 0, DateTime? enqueuedAt = null)
    {
        var row = new NfeEmailMessage
        {
            DocumentType = NfeEmailDocumentType.Sales, DocumentKey = Guid.NewGuid(), EventType = NfeEmailEventType.Authorized,
            Trigger = trigger, Status = status, CreatedAt = createdAt, CreatedBy = "tester",
            AttemptCount = attempts, EnqueuedAt = enqueuedAt,
        };
        _db.Context.NfeEmailMessages.Add(row);
        _db.Context.SaveChanges();
        return row;
    }

    private Task SweepAsync() =>
        new NfeEmailSweepJob(_db.Context, _jobs, new FakeCompanyContext("0002"), NullLogger<NfeEmailSweepJob>.Instance)
            .ExecuteAsync(CancellationToken.None);

    /// <summary>Retentativa é do Hangfire (30/120/600 s); o varredor não pode encurtar o prazo nem contar tentativa a mais.</summary>
    [Fact]
    public async Task Does_not_reenqueue_pending_row_already_attempted()
    {
        Seed(NfeEmailStatus.Pending, DateTime.Now.AddMinutes(-5), attempts: 1, enqueuedAt: DateTime.Now.AddHours(-2));

        await SweepAsync();

        Assert.Empty(_jobs.Enqueued);
    }

    [Fact]
    public async Task Does_not_double_enqueue_row_waiting_in_queue()
    {
        Seed(NfeEmailStatus.Pending, DateTime.Now.AddMinutes(-5));

        await SweepAsync();
        await SweepAsync();

        Assert.Single(_jobs.Enqueued);
    }

    [Fact]
    public async Task Reenqueues_never_attempted_row_after_stale_window()
    {
        var row = Seed(NfeEmailStatus.Pending, DateTime.Now.AddHours(-2),
            enqueuedAt: DateTime.Now - NfeEmailSweepJob.ReenqueueAfter - TimeSpan.FromMinutes(1));

        await SweepAsync();

        Assert.Equal(row.Key, Assert.Single(_jobs.EnqueuedOutboxKeys));
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
