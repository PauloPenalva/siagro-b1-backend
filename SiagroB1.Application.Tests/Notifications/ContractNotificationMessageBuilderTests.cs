using System.Globalization;
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
        Assert.Contains("500.000 KG", message);
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
        Assert.Contains("Volume: 100.000 KG", message);
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

    /// <summary>
    /// Mesmo cadastro que o diálogo de faturamento usa (ITEM_COMPLEMENTS): o fator é "KG por
    /// unidade comercial". O KG fica entre parênteses porque é a verdade física do contrato.
    /// </summary>
    private static ContractNotificationPayload CommercialPayload(
        NotificationEventType eventType = NotificationEventType.Created)
    {
        var payload = Payload(eventType);
        payload.CommercialUnitOfMeasureCode = "SC";
        payload.CommercialFactor = 60m;
        return payload;
    }

    [Fact]
    public void Build_WithCommercialUnit_ShowsVolumeInCommercialUnitAndKg()
    {
        var message = ContractNotificationMessageBuilder.Build(CommercialPayload());

        Assert.Contains("Volume: 8.333,33 SC (500.000 KG)", message);
    }

    [Fact]
    public void Build_WithCommercialUnit_ShowsPricePerCommercialUnit()
    {
        var message = ContractNotificationMessageBuilder.Build(CommercialPayload());

        Assert.Contains("Preço: R$ 150,00 / SC", message);
    }

    /// <summary>
    /// Produto sem complemento (ou payload gravado antes desta mudança): segue em KG, como era.
    /// </summary>
    [Fact]
    public void Build_WithoutCommercialUnit_KeepsKg()
    {
        var message = ContractNotificationMessageBuilder.Build(Payload());

        Assert.Contains("Volume: 500.000 KG", message);
        Assert.Contains("Preço: R$ 2,50", message);
        Assert.DoesNotContain(" / ", message);
    }

    [Fact]
    public void Build_PriceFixationWithCommercialUnit_ConvertsFixationVolumeAndPrice()
    {
        var payload = CommercialPayload(NotificationEventType.PriceFixationApproved);
        payload.FixationVolume = 100_000m;
        payload.FixationPrice = 2.75m;

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Volume: 1.666,67 SC (100.000 KG)", message);
        Assert.Contains("Preço: R$ 165,00 / SC", message);
    }

    [Fact]
    public void Build_HeaderUpdatedWithCommercialUnit_ConvertsVolumeAndPriceChanges()
    {
        var payload = CommercialPayload(NotificationEventType.HeaderUpdated);
        payload.FieldChanges =
        [
            new()
            {
                Field = "TotalVolume", Label = "Volume total",
                OldValue = "400.000,000", NewValue = "500.000,000",
                OldNumber = 400_000m, NewNumber = 500_000m,
            },
            new()
            {
                Field = "StandardPrice", Label = "Preço",
                OldValue = "2,40", NewValue = "2,50",
                OldNumber = 2.40m, NewNumber = 2.50m,
            },
        ];

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Volume total: 6.666,67 SC (400.000 KG) → 8.333,33 SC (500.000 KG)", message);
        Assert.Contains("Preço: 144,00 / SC → 150,00 / SC", message);
    }

    /// <summary>
    /// GAC-1087: casas decimais só quando existem, no máximo duas — "1.000,000 SC" vira
    /// "1.000 SC" e "1.234,600 SC" vira "1.234,6 SC". Vale para a unidade comercial e para o KG.
    /// </summary>
    [Theory]
    [InlineData(60_000, "Volume: 1.000 SC (60.000 KG)")]
    [InlineData(74_076, "Volume: 1.234,6 SC (74.076 KG)")]
    [InlineData(500_000, "Volume: 8.333,33 SC (500.000 KG)")]
    public void Build_WithCommercialUnit_PrintsDecimalsOnlyWhenPresent(int totalVolume, string expected)
    {
        var payload = CommercialPayload();
        payload.TotalVolume = totalVolume;

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains(expected, message);
    }

    [Theory]
    [InlineData("1234.567", "Volume: 1.234,57 KG")]
    [InlineData("1234.600", "Volume: 1.234,6 KG")]
    [InlineData("1234.000", "Volume: 1.234 KG")]
    public void Build_WithoutCommercialUnit_PrintsAtMostTwoDecimalsInKg(string totalVolume, string expected)
    {
        var payload = Payload();
        payload.TotalVolume = decimal.Parse(totalVolume, CultureInfo.InvariantCulture);

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains(expected, message);
    }

    /// <summary>
    /// Sem unidade comercial a alteração de volume também sai sem zeros à direita: com o número
    /// cru no payload, não depende do texto em 3 casas que o diff grava.
    /// </summary>
    [Fact]
    public void Build_HeaderUpdatedWithoutCommercialUnit_PrintsVolumeChangesWithoutTrailingZeros()
    {
        var payload = Payload(NotificationEventType.HeaderUpdated);
        payload.FieldChanges =
        [
            new()
            {
                Field = "TotalVolume", Label = "Volume total",
                OldValue = "400.000,000", NewValue = "500.000,000",
                OldNumber = 400_000m, NewNumber = 500_000m,
            },
        ];

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Volume total: 400.000 KG → 500.000 KG", message);
    }

    /// <summary>
    /// O frete tem unidade própria (FreightUmCode); converter pelo fator do produto daria um
    /// número sem sentido.
    /// </summary>
    [Fact]
    public void Build_HeaderUpdatedWithCommercialUnit_DoesNotConvertFreightCost()
    {
        var payload = CommercialPayload(NotificationEventType.HeaderUpdated);
        payload.FieldChanges =
        [
            new()
            {
                Field = "FreightCostStandard", Label = "Custo do frete",
                OldValue = "10,00", NewValue = "12,00",
                OldNumber = 10m, NewNumber = 12m,
            },
        ];

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Custo do frete: 10,00 → 12,00", message);
    }

    /// <summary>
    /// Alteração gravada na outbox antes desta mudança não tem os números crus: cai para o texto.
    /// </summary>
    [Fact]
    public void Build_HeaderUpdatedWithoutRawNumbers_FallsBackToFormattedText()
    {
        var payload = CommercialPayload(NotificationEventType.HeaderUpdated);
        payload.FieldChanges =
        [
            new() { Field = "TotalVolume", Label = "Volume total", OldValue = "400.000,000", NewValue = "500.000,000" },
        ];

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Volume total: 400.000,000 → 500.000,000", message);
    }

    [Fact]
    public void Build_ShowsBranchNameInsteadOfCode()
    {
        var payload = Payload();
        payload.BranchName = "Filial Pilar";

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Filial: Filial Pilar", message);
        Assert.DoesNotContain("Filial: 01", message);
    }

    [Fact]
    public void Build_WithoutBranchName_FallsBackToBranchCode()
    {
        var message = ContractNotificationMessageBuilder.Build(Payload());

        Assert.Contains("Filial: 01", message);
    }

    /// <summary>
    /// O WhatsApp só transforma em link um endereço com nome de domínio. Com IP (caso real da
    /// produção Yokotobi) ele destaca só "192.168.1.144", sem porta nem caminho — um link que não
    /// leva a lugar nenhum é pior do que nenhum.
    /// </summary>
    [Theory]
    [InlineData("http://192.168.1.144:55000/#/purchase-contracts/abc/detail")]
    [InlineData("http://localhost:5246/#/purchase-contracts/abc/detail")]
    [InlineData("http://[::1]:5246/#/purchase-contracts/abc/detail")]
    [InlineData("http://[2001:db8::1]/#/purchase-contracts/abc/detail")]
    [InlineData("http://servidor:5246/#/purchase-contracts/abc/detail")]
    [InlineData("siagro.exemplo.com.br/#/purchase-contracts/abc/detail")]
    public void Build_LinkThatWhatsAppCannotMakeClickable_IsOmitted(string detailUrl)
    {
        var payload = Payload();
        payload.DetailUrl = detailUrl;

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.DoesNotContain("purchase-contracts", message);
        Assert.Equal(message.TrimEnd(), message);
    }

    /// <summary>
    /// GAC-1087: emissão, previsão de pagamento e condição de pagamento entram no bloco de dados,
    /// na ordem do formulário — emissão logo abaixo do parceiro, pagamento logo abaixo do preço.
    /// </summary>
    private static ContractNotificationPayload PaymentPayload()
    {
        var payload = Payload();
        payload.CreationDate = new DateTime(2026, 7, 20);
        payload.PaymentForecastDate = new DateTime(2026, 9, 15);
        payload.PaymentTerms = "30 dias após a entrega";
        return payload;
    }

    private static List<string> Lines(string message) =>
        message.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    [Fact]
    public void Build_ShowsIssueDateRightAfterPartner()
    {
        var lines = Lines(ContractNotificationMessageBuilder.Build(PaymentPayload()));

        var partnerIndex = lines.IndexOf("Fornecedor: F0001 - AGRO XPTO LTDA");
        Assert.True(partnerIndex >= 0);
        Assert.Equal("Emissão: 20/07/2026", lines[partnerIndex + 1]);
        Assert.StartsWith("Produto:", lines[partnerIndex + 2]);
    }

    [Fact]
    public void Build_ShowsPaymentForecastAndTermsRightAfterPrice()
    {
        var lines = Lines(ContractNotificationMessageBuilder.Build(PaymentPayload()));

        var priceIndex = lines.IndexOf("Preço: R$ 2,50");
        Assert.True(priceIndex >= 0);
        Assert.Equal("Prev. Pagto.: 15/09/2026", lines[priceIndex + 1]);
        Assert.Equal("Cond. Pagamento: 30 dias após a entrega", lines[priceIndex + 2]);
    }

    /// <summary>
    /// Payload gravado antes desta mudança, ou contrato sem os campos: a linha some, não sai "-".
    /// </summary>
    [Fact]
    public void Build_WithoutPaymentFields_OmitsTheirLines()
    {
        var message = ContractNotificationMessageBuilder.Build(Payload());

        Assert.DoesNotContain("Emissão", message);
        Assert.DoesNotContain("Prev. Pagto.", message);
        Assert.DoesNotContain("Cond. Pagamento", message);
    }

    /// <summary>
    /// A condição de pagamento vem de um TextArea: espaço e quebra de linha sobrando nas pontas
    /// deixariam uma linha em branco dentro do bloco de dados.
    /// </summary>
    [Fact]
    public void Build_PaymentTerms_IsTrimmedAndBlankIsOmitted()
    {
        var payload = PaymentPayload();
        payload.PaymentTerms = "  À vista\n  ";
        Assert.Contains("Cond. Pagamento: À vista", Lines(ContractNotificationMessageBuilder.Build(payload)));

        payload.PaymentTerms = "   ";
        Assert.DoesNotContain("Cond. Pagamento", ContractNotificationMessageBuilder.Build(payload));
    }

    /// <summary>Na alteração o bloco de dados dá lugar à lista — as linhas novas não podem vazar.</summary>
    [Fact]
    public void Build_HeaderUpdated_DoesNotRepeatPaymentFields()
    {
        var payload = PaymentPayload();
        payload.EventType = NotificationEventType.HeaderUpdated;
        payload.FieldChanges =
        [
            new() { Field = "PaymentTerms", Label = "Condição de pagamento", OldValue = "À vista", NewValue = "30 dias" },
        ];

        var message = ContractNotificationMessageBuilder.Build(payload);

        Assert.Contains("Condição de pagamento: À vista → 30 dias", message);
        Assert.DoesNotContain("Emissão", message);
        Assert.DoesNotContain("Cond. Pagamento:", message);
    }
}
