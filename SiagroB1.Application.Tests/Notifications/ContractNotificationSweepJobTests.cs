using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// O varredor é o mecanismo de ENTREGA, não uma rede de segurança: enfileirar direto no
/// serviço de mutação não funcionaria, porque o Hangfire grava em conexão própria com
/// autocommit e o worker poderia ler a outbox antes do COMMIT da transação de negócio.
///
/// A janela de carência existe por isso — dá tempo do COMMIT acontecer.
/// </summary>
public class ContractNotificationSweepJobTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeBackgroundJobClient _jobClient = new();

    private ContractNotificationSweepJob CreateJob() =>
        new(_db.Context, _jobClient, NullLogger<ContractNotificationSweepJob>.Instance);

    private NotificationOutboxMessage Seed(NotificationOutboxStatus status, DateTime occurredAt)
    {
        var message = new NotificationOutboxMessage
        {
            Key = Guid.NewGuid(),
            DocumentType = NotificationDocumentType.PurchaseContract,
            EventType = NotificationEventType.Approved,
            DocumentKey = Guid.NewGuid(),
            PayloadJson = "{}",
            Status = status,
            OccurredAt = occurredAt,
        };

        _db.Context.NotificationOutboxMessages.Add(message);
        _db.Context.SaveChanges();

        return message;
    }

    [Fact]
    public async Task ExecuteAsync_PendingMessagePastGracePeriod_IsEnqueued()
    {
        var message = Seed(NotificationOutboxStatus.Pending, DateTime.Now.AddMinutes(-5));

        await CreateJob().ExecuteAsync();

        Assert.Equal(message.Key, Assert.Single(_jobClient.EnqueuedOutboxKeys));
    }

    /// <summary>
    /// Recém-criada, a linha pode ainda estar dentro de uma transação não commitada. Enfileirar
    /// agora faria o worker não encontrá-la e desistir em silêncio.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PendingMessageStillInGracePeriod_IsNotEnqueued()
    {
        Seed(NotificationOutboxStatus.Pending, DateTime.Now);

        await CreateJob().ExecuteAsync();

        Assert.Empty(_jobClient.EnqueuedOutboxKeys);
    }

    [Theory]
    [InlineData(NotificationOutboxStatus.Sent)]
    [InlineData(NotificationOutboxStatus.Failed)]
    [InlineData(NotificationOutboxStatus.PartiallySent)]
    [InlineData(NotificationOutboxStatus.Skipped)]
    public async Task ExecuteAsync_ResolvedMessage_IsNotEnqueued(NotificationOutboxStatus status)
    {
        Seed(status, DateTime.Now.AddMinutes(-5));

        await CreateJob().ExecuteAsync();

        Assert.Empty(_jobClient.EnqueuedOutboxKeys);
    }

    /// <summary>
    /// Lote limitado: um backlog grande (feature religada após dias desligada) não pode virar
    /// uma rajada única — é assim que se queima o número no provedor.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_LargeBacklog_EnqueuesAtMostOneBatch()
    {
        for (var i = 0; i < 250; i++)
            Seed(NotificationOutboxStatus.Pending, DateTime.Now.AddMinutes(-10));

        await CreateJob().ExecuteAsync();

        Assert.Equal(200, _jobClient.EnqueuedOutboxKeys.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ProcessesOldestFirst()
    {
        var newest = Seed(NotificationOutboxStatus.Pending, DateTime.Now.AddMinutes(-5));
        var oldest = Seed(NotificationOutboxStatus.Pending, DateTime.Now.AddMinutes(-30));

        await CreateJob().ExecuteAsync();

        Assert.Equal([oldest.Key, newest.Key], _jobClient.EnqueuedOutboxKeys);
    }
}
