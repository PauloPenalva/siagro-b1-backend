using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// O job de envio. A regra que mais importa: ele só relança quando TODAS as falhas são
/// transitórias. Relançar por um 4xx faria o Hangfire repetir uma mensagem que nunca vai
/// entregar — queimando quota do provedor e adiando a descoberta de que o cadastro está errado.
/// </summary>
public class ContractNotificationDispatchJobTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractNotificationDispatchJob CreateJob(FakeWhatsAppSender sender, bool enabled = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:WhatsApp:Enabled"] = enabled.ToString(),
            })
            .Build();

        return new ContractNotificationDispatchJob(
            _db.Context,
            new NotificationRecipientResolver(_db.Context),
            sender,
            configuration,
            NullLogger<ContractNotificationDispatchJob>.Instance);
    }

    private NotificationOutboxMessage SeedOutbox(
        NotificationOutboxStatus status = NotificationOutboxStatus.Pending)
    {
        var payload = new ContractNotificationPayload
        {
            DocumentType = NotificationDocumentType.PurchaseContract,
            EventType = NotificationEventType.Approved,
            ContractKey = Guid.NewGuid(),
            ContractCode = "PC-000123",
            CardName = "AGRO XPTO LTDA",
            TotalVolume = 500_000m,
            UnitOfMeasureCode = "KG",
            OccurredAt = DateTime.Now,
        };

        var message = new NotificationOutboxMessage
        {
            Key = Guid.NewGuid(),
            DocumentType = NotificationDocumentType.PurchaseContract,
            EventType = NotificationEventType.Approved,
            DocumentKey = payload.ContractKey,
            DocumentCode = "PC-000123",
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = status,
        };

        _db.Context.NotificationOutboxMessages.Add(message);
        _db.Context.SaveChanges();

        return message;
    }

    /// <summary>Cadastra um grupo assinando Compra/Aprovado com os telefones informados.</summary>
    private void SeedSubscribers(params string[] phones)
    {
        var group = new NotificationGroup
        {
            Key = Guid.NewGuid(), Code = "COM", Name = "Comercial", Active = true,
        };

        _db.Context.NotificationGroups.Add(group);
        _db.Context.NotificationGroupSubscriptions.Add(new NotificationGroupSubscription
        {
            Key = Guid.NewGuid(),
            NotificationGroupKey = group.Key,
            DocumentType = NotificationDocumentType.PurchaseContract,
            EventType = NotificationEventType.Approved,
        });

        foreach (var phone in phones)
        {
            _db.Context.NotificationGroupMembers.Add(new NotificationGroupMember
            {
                Key = Guid.NewGuid(),
                NotificationGroupKey = group.Key,
                Name = $"Pessoa {phone[^4..]}",
                Phone = phone,
                PhoneE164 = phone,
                Active = true,
            });
        }

        _db.Context.SaveChanges();
    }

    private NotificationOutboxMessage Reload(Guid key) =>
        _db.Context.NotificationOutboxMessages.Single(m => m.Key == key);

    /// <summary>
    /// A linha pode não existir: o varredor enfileira e, entre isso e a execução, um reenvio
    /// concorrente ou uma exclusão pode ter acontecido. Sair calado é o comportamento certo.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_UnknownOutboxKey_DoesNothing()
    {
        var sender = new FakeWhatsAppSender();

        await CreateJob(sender).ExecuteAsync(Guid.NewGuid());

        Assert.Empty(sender.Sent);
    }

    /// <summary>
    /// Idempotência: o varredor roda a cada minuto e pode enfileirar a mesma linha duas vezes
    /// se um envio demorar. Processar de novo mandaria a mensagem repetida.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlreadySent_DoesNotSendAgain()
    {
        var message = SeedOutbox(NotificationOutboxStatus.Sent);
        SeedSubscribers("5566999998888");
        var sender = new FakeWhatsAppSender();

        await CreateJob(sender).ExecuteAsync(message.Key);

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task ExecuteAsync_AllRecipientsReceive_MarksSentAndLogsEachDelivery()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998881", "5566999998882", "5566999998883");
        var sender = new FakeWhatsAppSender();

        await CreateJob(sender).ExecuteAsync(message.Key);

        Assert.Equal(3, sender.Sent.Count);
        Assert.Equal(3, _db.Context.NotificationDeliveryLogs.Count());
        Assert.Equal(NotificationOutboxStatus.Sent, Reload(message.Key).Status);
    }

    [Fact]
    public async Task ExecuteAsync_SendsRenderedMessageContainingContractData()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998888");
        var sender = new FakeWhatsAppSender();

        await CreateJob(sender).ExecuteAsync(message.Key);

        var (_, text) = Assert.Single(sender.Sent);
        Assert.Contains("PC-000123", text);
        Assert.Contains("AGRO XPTO LTDA", text);
        Assert.Contains("APROVADO", text);
    }

    /// <summary>
    /// Falha permanente em parte dos destinatários não pode arrastar quem já recebeu para uma
    /// retentativa — daria mensagem duplicada.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_OnePermanentFailure_IsPartiallySentAndDoesNotThrow()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998881", "5566999998882");
        var sender = new FakeWhatsAppSender().FailsPermanently("5566999998882");

        await CreateJob(sender).ExecuteAsync(message.Key);

        Assert.Equal(NotificationOutboxStatus.PartiallySent, Reload(message.Key).Status);
        Assert.Equal(1, _db.Context.NotificationDeliveryLogs.Count(l => l.Status == NotificationDeliveryStatus.Failed));
        Assert.Equal(1, _db.Context.NotificationDeliveryLogs.Count(l => l.Status == NotificationDeliveryStatus.Sent));
    }

    [Fact]
    public async Task ExecuteAsync_AllPermanentFailures_IsFailedAndDoesNotThrow()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998881");
        var sender = new FakeWhatsAppSender().FailsPermanently("5566999998881");

        await CreateJob(sender).ExecuteAsync(message.Key);

        Assert.Equal(NotificationOutboxStatus.Failed, Reload(message.Key).Status);
    }

    /// <summary>
    /// Só aqui vale retentar — e relançar é como se pede retentativa ao Hangfire.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AllTransientFailures_ThrowsSoHangfireRetries()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998881");
        var sender = new FakeWhatsAppSender().FailsTransiently("5566999998881");

        // Tipo específico de propósito: ThrowsAnyAsync<Exception> passaria também com o
        // NotImplementedException de um stub, escondendo que o job nem chegou a rodar.
        await Assert.ThrowsAsync<ApplicationException>(() => CreateJob(sender).ExecuteAsync(message.Key));

        Assert.Single(sender.Sent);
        // Continua pendente: a retentativa do Hangfire precisa reprocessá-la.
        Assert.Equal(NotificationOutboxStatus.Pending, Reload(message.Key).Status);
    }

    [Fact]
    public async Task ExecuteAsync_NoSubscribers_IsSkippedWithoutCallingProvider()
    {
        var message = SeedOutbox();
        var sender = new FakeWhatsAppSender();

        await CreateJob(sender).ExecuteAsync(message.Key);

        Assert.Empty(sender.Sent);
        Assert.Equal(NotificationOutboxStatus.Skipped, Reload(message.Key).Status);
    }

    /// <summary>
    /// Kill-switch. Marca Skipped em vez de deixar Pending: pendente, o varredor reenfileiraria
    /// a linha a cada minuto para sempre, e religar a feature despejaria todo o acumulado de
    /// uma vez no WhatsApp das pessoas.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_FeatureDisabled_SkipsWithoutCallingProvider()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998888");
        var sender = new FakeWhatsAppSender();

        await CreateJob(sender, enabled: false).ExecuteAsync(message.Key);

        Assert.Empty(sender.Sent);
        Assert.Equal(NotificationOutboxStatus.Skipped, Reload(message.Key).Status);
    }

    [Fact]
    public async Task ExecuteAsync_DeliveryLog_KeepsRecipientAndMessageSnapshot()
    {
        var message = SeedOutbox();
        SeedSubscribers("5566999998888");

        await CreateJob(new FakeWhatsAppSender()).ExecuteAsync(message.Key);

        var log = Assert.Single(_db.Context.NotificationDeliveryLogs);
        Assert.Equal("5566999998888", log.RecipientPhone);
        Assert.Equal("Comercial", log.GroupName);
        Assert.Equal("MSG-1", log.ProviderMessageId);
        Assert.Contains("PC-000123", log.MessageText);
        Assert.Equal(1, log.Attempt);
    }
}
