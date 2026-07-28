using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Cada operação de negócio do contrato de compra grava UMA linha na outbox, com o evento
/// correto — e os serviços de recálculo não gravam nenhuma.
///
/// Esse último ponto é o que sustenta a escolha por registro explícito em vez de um
/// interceptor que inferisse os eventos: <c>RecalculateBalanceService</c> percorre contratos em
/// lote gravando <c>AllocatedVolume</c>, e um interceptor transformaria isso em centenas de
/// mensagens de WhatsApp.
/// </summary>
public class PurchaseContractNotificationEventTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractNotificationOutboxService Outbox()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:AppBaseUrl"] = "https://siagro.teste" })
            .Build();

        return new ContractNotificationOutboxService(
            _db.Context, new ContractNotificationPayloadBuilder(configuration));
    }

    private PurchaseContract Seed(ContractStatus status)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-000123",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 500_000m,
            StandardPrice = 2.5m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.SaveChanges();

        return contract;
    }

    private NotificationOutboxMessage SingleOutboxMessage() =>
        Assert.Single(_db.Context.NotificationOutboxMessages);

    [Fact]
    public async Task SendApproval_RegistersSentForApproval()
    {
        var contract = Seed(ContractStatus.Draft);

        await new PurchaseContractsSendApprovalService(_db.Context, Outbox())
            .ExecuteAsync(contract.Key, "paulo");

        Assert.Equal(NotificationEventType.SentForApproval, SingleOutboxMessage().EventType);
    }

    [Fact]
    public async Task Approval_RegistersApproved()
    {
        var contract = Seed(ContractStatus.InApproval);

        await new PurchaseContractsApprovalService(_db.Context, Outbox())
            .ExecuteAsync(contract.Key, "ok", "paulo");

        var message = SingleOutboxMessage();
        Assert.Equal(NotificationEventType.Approved, message.EventType);
        Assert.Equal(NotificationDocumentType.PurchaseContract, message.DocumentType);
        Assert.Equal(contract.Key, message.DocumentKey);
        Assert.Equal("paulo", message.TriggeredBy);
    }

    [Fact]
    public async Task Reject_RegistersRejected()
    {
        var contract = Seed(ContractStatus.InApproval);

        await new PurchaseContractsRejectService(_db.Context, Outbox())
            .ExecuteAsync(contract.Key, "sem margem", "paulo");

        Assert.Equal(NotificationEventType.Rejected, SingleOutboxMessage().EventType);
    }

    [Fact]
    public async Task Reopen_RegistersReopened()
    {
        var contract = Seed(ContractStatus.Finished);

        await new PurchaseContractsReopenService(_db.Context, Outbox())
            .ExecuteAsync(contract.Key, "paulo");

        Assert.Equal(NotificationEventType.Reopened, SingleOutboxMessage().EventType);
    }

    /// <summary>
    /// O teste que justifica o desenho. O recálculo de saldo grava <c>AllocatedVolume</c> em
    /// contratos em lote; se isso gerasse evento, uma manutenção de rotina viraria spam.
    /// </summary>
    [Fact]
    public async Task RecalculateBalance_RegistersNothing()
    {
        var contract = Seed(ContractStatus.Approved);

        await new PurchaseContractsRecalculateBalanceService(_db.Context)
            .ExecuteAsync(contract.Key);

        Assert.Empty(_db.Context.NotificationOutboxMessages);
    }
}
