using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Notifications;

/// <summary>
/// A mensagem é o produto final da feature: é o que a pessoa lê no celular, sem tela nenhuma
/// para traduzir ou reformatar nada depois.
/// </summary>
public class ContractNotificationMessageBuilderTests
{
    private static ContractNotificationPayload Payload(
        NotificationEventType eventType = NotificationEventType.Created,
        NotificationDocumentType documentType = NotificationDocumentType.PurchaseContract) => new()
    {
        DocumentType = documentType,
        EventType = eventType,
        ContractKey = Guid.NewGuid(),
        ContractCode = "000123",
        CardCode = "F0001",
        CardName = "AGRO XPTO LTDA",
        ItemCode = "SOJA",
        ItemName = "SOJA EM GRAOS",
        TotalVolume = 500_000m,
        UnitOfMeasureCode = "KG",
        Price = 2.50m,
        CurrencyCode = "BRL",
        DeliveryStartDate = new DateTime(2026, 8, 1),
        DeliveryEndDate = new DateTime(2026, 8, 31),
        BranchCode = "01",
        StatusLabel = "Rascunho",
        TriggeredBy = "paulo.penalva",
        OccurredAt = new DateTime(2026, 7, 28, 14, 32, 0),
        DetailUrl = "https://siagro.exemplo.com.br/#/purchase-contracts/abc/detail",
    };

    [Fact]
    public void Build_Created_HasHeaderWithDocumentCodeAndEvent()
    {
        var message = ContractNotificationMessageBuilder.Build(Payload());

        Assert.Contains("CONTRATO DE COMPRA", message);
        Assert.Contains("000123", message);
        Assert.Contains("INCLUÍDO", message);
    }

    [Fact]
    public void Build_Created_HasContractDataBlock()
    {
        var message = ContractNotificationMessageBuilder.Build(Payload());

        Assert.Contains("AGRO XPTO LTDA", message);
        Assert.Contains("SOJA EM GRAOS", message);
        Assert.Contains("500.000,000 KG", message);
        Assert.Contains("2,50", message);
        Assert.Contains("01/08/2026", message);
        Assert.Contains("31/08/2026", message);
        Assert.Contains("paulo.penalva", message);
    }

    /// <summary>
    /// O WhatsApp gera a prévia a partir da ÚLTIMA URL do texto. Link no meio da mensagem
    /// deixaria a prévia colada num bloco de dados e empurraria o resto para baixo do "ler mais".
    /// </summary>
    [Fact]
    public void Build_LinkIsTheLastLine()
    {
        var message = ContractNotificationMessageBuilder.Build(Payload());

        var lastLine = message.Split('\n').Last(line => !string.IsNullOrWhiteSpace(line));
        Assert.Equal("https://siagro.exemplo.com.br/#/purchase-contracts/abc/detail", lastLine.Trim());
    }

    [Fact]
    public void Build_WithoutDetailUrl_OmitsLinkAndDoesNotLeaveTrailingBlank()
    {
        var payload = Payload();
        payload.DetailUrl = null;

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.DoesNotContain("http", message);
        Assert.Equal(message.TrimEnd(), message);
    }

    /// <summary>
    /// Na alteração, o que interessa é o que mudou — repetir o contrato inteiro esconderia a
    /// informação nova no meio de dados que já eram conhecidos.
    /// </summary>
    [Fact]
    public void Build_HeaderUpdated_ReplacesDataBlockWithChangeList()
    {
        var payload = Payload(NotificationEventType.HeaderUpdated);
        payload.FieldChanges =
        [
            new() { Field = "TotalVolume", Label = "Volume total", OldValue = "400.000,000", NewValue = "500.000,000" },
            new() { Field = "StandardPrice", Label = "Preço", OldValue = "2,40", NewValue = "2,50" },
        ];

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("ALTERADO", message);
        Assert.Contains("Volume total: 400.000,000 → 500.000,000", message);
        Assert.Contains("Preço: 2,40 → 2,50", message);
        // O bloco de dados do contrato sai de cena — sobra só a identificação do parceiro.
        Assert.DoesNotContain("SOJA EM GRAOS", message);
    }

    /// <summary>
    /// Sem mudanças detectadas não pode sobrar um cabeçalho "Alterações:" vazio pendurado.
    /// </summary>
    [Fact]
    public void Build_HeaderUpdatedWithNoChanges_DoesNotRenderOrphanSection()
    {
        var payload = Payload(NotificationEventType.HeaderUpdated);

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.DoesNotContain("Alterações", message);
    }

    [Fact]
    public void Build_PriceFixationEvent_ShowsFixationVolumeAndPrice()
    {
        var payload = Payload(NotificationEventType.PriceFixationApproved);
        payload.FixationVolume = 100_000m;
        payload.FixationPrice = 2.75m;
        payload.FixationStatusLabel = "Confirmada";

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Fixação de preço aprovada".ToUpperInvariant(), message);
        Assert.Contains("100.000,000", message);
        Assert.Contains("2,75", message);
        Assert.Contains("Confirmada", message);
    }

    [Fact]
    public void Build_SalesContract_CallsPartnerCustomer()
    {
        var message = ContractNotificationMessageBuilder.Build(
            Payload(documentType: NotificationDocumentType.SalesContract));

        Assert.Contains("CONTRATO DE VENDA", message);
        Assert.Contains("Cliente:", message);
    }
}
