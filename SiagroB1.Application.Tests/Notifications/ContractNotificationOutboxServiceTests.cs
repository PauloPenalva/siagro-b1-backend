using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// A outbox é a porta única de escrita da notificação. O contrato do serviço é o mesmo do
/// <c>ChangeLogService</c> que já existe: ele só põe a linha no ChangeTracker e quem salva é o
/// caller — é assim que a notificação nasce e morre com a transação do contrato.
/// </summary>
public class ContractNotificationOutboxServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractNotificationOutboxService CreateService(string? appBaseUrl = "https://siagro.teste")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:AppBaseUrl"] = appBaseUrl })
            .Build();

        return new ContractNotificationOutboxService(
            _db.Context, new ContractNotificationPayloadBuilder(configuration));
    }

    private static PurchaseContract NewPurchaseContract() => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-000123",
        CardCode = "F0001",
        CardName = "AGRO XPTO LTDA",
        ItemCode = "SOJA",
        ItemName = "SOJA EM GRAOS",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 500_000m,
        StandardPrice = 2.50m,
        Status = ContractStatus.Draft,
    };

    private static ContractNotificationPayload PayloadOf(NotificationOutboxMessage message) =>
        JsonSerializer.Deserialize<ContractNotificationPayload>(message.PayloadJson)!;

    /// <summary>
    /// O ponto do desenho: sem o SaveChanges do caller não existe linha. É o que impede que uma
    /// operação revertida notifique alguém.
    /// </summary>
    [Fact]
    public async Task Register_DoesNotPersistOnItsOwn()
    {
        CreateService().Register(NewPurchaseContract(), NotificationEventType.Created, "paulo");

        Assert.Empty(await Task.FromResult(_db.Context.NotificationOutboxMessages.ToList()));
    }

    [Fact]
    public async Task Register_AfterCallerSaves_WritesOnePendingRow()
    {
        var contract = NewPurchaseContract();

        CreateService().Register(contract, NotificationEventType.Created, "paulo");
        await _db.SaveChangesAsync();

        var message = Assert.Single(_db.Context.NotificationOutboxMessages);
        Assert.Equal(NotificationDocumentType.PurchaseContract, message.DocumentType);
        Assert.Equal(NotificationEventType.Created, message.EventType);
        Assert.Equal(contract.Key, message.DocumentKey);
        Assert.Equal("PC-000123", message.DocumentCode);
        Assert.Equal("paulo", message.TriggeredBy);
        Assert.Equal(NotificationOutboxStatus.Pending, message.Status);
    }

    [Fact]
    public async Task Register_SnapshotsContractDataIntoPayload()
    {
        CreateService().Register(NewPurchaseContract(), NotificationEventType.Created, "paulo");
        await _db.SaveChangesAsync();

        var payload = PayloadOf(_db.Context.NotificationOutboxMessages.Single());

        Assert.Equal("PC-000123", payload.ContractCode);
        Assert.Equal("AGRO XPTO LTDA", payload.CardName);
        Assert.Equal(500_000m, payload.TotalVolume);
        Assert.Equal(2.50m, payload.Price);
    }

    [Fact]
    public async Task Register_BuildsDetailUrlForPurchaseContractRoute()
    {
        var contract = NewPurchaseContract();

        CreateService().Register(contract, NotificationEventType.Created, "paulo");
        await _db.SaveChangesAsync();

        var payload = PayloadOf(_db.Context.NotificationOutboxMessages.Single());

        Assert.Equal($"https://siagro.teste/#/purchase-contracts/{contract.Key}/detail", payload.DetailUrl);
    }

    /// <summary>
    /// Sem URL base configurada a mensagem sai sem link, e não com um link quebrado.
    /// </summary>
    [Fact]
    public async Task Register_WithoutConfiguredBaseUrl_LeavesDetailUrlEmpty()
    {
        CreateService(appBaseUrl: null).Register(NewPurchaseContract(), NotificationEventType.Created, "paulo");
        await _db.SaveChangesAsync();

        Assert.True(string.IsNullOrEmpty(PayloadOf(_db.Context.NotificationOutboxMessages.Single()).DetailUrl));
    }

    [Fact]
    public async Task Register_HeaderUpdated_CarriesFieldChangesIntoPayload()
    {
        List<ContractNotificationFieldChange> changes =
        [
            new() { Field = "TotalVolume", Label = "Volume total", OldValue = "400.000,000", NewValue = "500.000,000" },
        ];

        CreateService().Register(NewPurchaseContract(), NotificationEventType.HeaderUpdated, "paulo", changes);
        await _db.SaveChangesAsync();

        var change = Assert.Single(PayloadOf(_db.Context.NotificationOutboxMessages.Single()).FieldChanges);
        Assert.Equal("Volume total", change.Label);
        Assert.Equal("500.000,000", change.NewValue);
    }

    [Fact]
    public async Task Register_SalesContract_UsesSalesRouteAndDocumentType()
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
        };

        CreateService().Register(contract, NotificationEventType.Approved, "paulo");
        await _db.SaveChangesAsync();

        var message = _db.Context.NotificationOutboxMessages.Single();
        Assert.Equal(NotificationDocumentType.SalesContract, message.DocumentType);
        Assert.Contains($"/#/sales-contracts/{contract.Key}/detail", PayloadOf(message).DetailUrl);
    }

    [Fact]
    public async Task RegisterPriceFixation_CarriesFixationVolumeAndPrice()
    {
        var contract = NewPurchaseContract();
        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            FixationVolume = 100_000m,
            FixationPrice = 2.75m,
            Status = PriceFixationStatus.Confirmed,
        };

        CreateService().RegisterPriceFixation(
            contract, fixation, NotificationEventType.PriceFixationApproved, "paulo");
        await _db.SaveChangesAsync();

        var payload = PayloadOf(_db.Context.NotificationOutboxMessages.Single());
        Assert.Equal(100_000m, payload.FixationVolume);
        Assert.Equal(2.75m, payload.FixationPrice);
        Assert.Equal("Confirmada", payload.FixationStatusLabel);
    }
}
