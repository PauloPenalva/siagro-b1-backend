using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Jobs.Reinf;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using Xunit;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfBatchQueryJobTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 13, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class RecordingService(bool failFirst) : IReinfService
    {
        public List<Guid> Queried { get; } = [];
        public Task QueryBatchAsync(Guid batchKey, CancellationToken ct)
        {
            Queried.Add(batchKey);
            if (failFirst && Queried.Count == 1) throw new InvalidOperationException("falhou");
            return Task.CompletedTask;
        }
        public Task<ReinfPeriodOverview> GetOverviewAsync(string r, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> TransmitAsync(string r, string p, string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid> SendRegistrationAsync(string r, string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid> CloseAsync(string r, string p, string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid> ReopenAsync(string r, string p, string u, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> ReleaseUnconfirmedAsync(string r, string? p, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Queries_only_eligible_batches_and_continues_after_failure()
    {
        var db = TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());
        ReinfBatch B(ReinfBatchStatus st, string? prot, int minutesAgo, int attempts = 0)
        {
            var b = new ReinfBatch { TaxIdRoot = "12345678", Status = st, Protocol = prot, SentAt = Now.AddMinutes(-minutesAgo), Attempts = attempts };
            db.Context.ReinfBatches.Add(b);
            return b;
        }
        var sent = B(ReinfBatchStatus.Sent, "P1", 5);
        var processing = B(ReinfBatchStatus.Processing, "P2", 3);
        B(ReinfBatchStatus.Sent, null, 5);
        B(ReinfBatchStatus.Sent, "P3", 0);
        B(ReinfBatchStatus.Sent, "P4", 5, attempts: 30);
        B(ReinfBatchStatus.Processed, "P5", 5);
        B(ReinfBatchStatus.Refused, "P6", 5);
        B(ReinfBatchStatus.Failed, "P7", 5);
        db.Context.SaveChanges();

        var service = new RecordingService(failFirst: true);
        var logger = new TestLogger<ReinfBatchQueryJob>();
        await new ReinfBatchQueryJob(db.Context, service, new FixedTime(), logger).RunAsync(default);

        Assert.Equal([sent.Key, processing.Key], service.Queried);
        Assert.Single(logger.Entries);
        Assert.Equal(1, db.Context.ReinfBatches.AsNoTracking().Single(b => b.Key == sent.Key).Attempts);
    }
}
