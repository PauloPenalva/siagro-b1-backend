using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Espelho de <c>PurchaseContractNotificationEventTests</c> para o contrato de VENDA. Existe
/// separado porque o par compra/venda é copiado à mão no projeto: um evento trocado só no lado
/// da venda passaria despercebido sem este arquivo.
/// </summary>
public class SalesContractNotificationEventTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContract Seed(ContractStatus status)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-000777",
            CardCode = "C0001",
            CardName = "CLIENTE XPTO",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100_000m,
            Price = 3m,
            Status = status,
        };

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SaveChanges();

        return contract;
    }

    private NotificationOutboxMessage SingleOutboxMessage() =>
        Assert.Single(_db.Context.NotificationOutboxMessages);

    [Fact]
    public async Task SendApproval_RegistersSentForApproval()
    {
        var contract = Seed(ContractStatus.Draft);

        await new SalesContractsSendApprovalService(_db.Context, TestNotificationOutbox.For(_db.Context))
            .ExecuteAsync(contract.Key, "paulo");

        Assert.Equal(NotificationEventType.SentForApproval, SingleOutboxMessage().EventType);
    }

    [Fact]
    public async Task Approval_RegistersApprovedForSalesDocumentType()
    {
        var contract = Seed(ContractStatus.InApproval);

        await new SalesContractsApprovalService(_db.Context, TestNotificationOutbox.For(_db.Context))
            .ExecuteAsync(contract.Key, "ok", "paulo");

        var message = SingleOutboxMessage();
        Assert.Equal(NotificationEventType.Approved, message.EventType);
        // O tipo de documento é o que separa quem assina compra de quem assina venda.
        Assert.Equal(NotificationDocumentType.SalesContract, message.DocumentType);
        Assert.Equal(contract.Key, message.DocumentKey);
    }

    [Fact]
    public async Task Reject_RegistersRejected()
    {
        var contract = Seed(ContractStatus.InApproval);

        await new SalesContractsRejectService(_db.Context, TestNotificationOutbox.For(_db.Context))
            .ExecuteAsync(contract.Key, "preço fora", "paulo");

        Assert.Equal(NotificationEventType.Rejected, SingleOutboxMessage().EventType);
    }

    [Fact]
    public async Task Reopen_RegistersReopened()
    {
        var contract = Seed(ContractStatus.Finished);

        await new SalesContractsReopenService(_db.Context, TestNotificationOutbox.For(_db.Context))
            .ExecuteAsync(contract.Key, "paulo");

        Assert.Equal(NotificationEventType.Reopened, SingleOutboxMessage().EventType);
    }

    [Fact]
    public async Task RecalculateBalance_RegistersNothing()
    {
        var contract = Seed(ContractStatus.Approved);

        await new SalesContractsRecalculateBalanceService(_db.Context).ExecuteAsync(contract.Key);

        Assert.Empty(_db.Context.NotificationOutboxMessages);
    }
}
