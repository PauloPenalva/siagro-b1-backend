using System.Globalization;
using System.Text;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Monta o texto que vai para o WhatsApp a partir do snapshot gravado na outbox.
///
/// Função pura do payload: não lê banco, não injeta nada. É o que permite testar a mensagem
/// sem subir contexto, e é o que garante que a mensagem descreva o contrato COMO ELE ESTAVA no
/// momento do evento — o envio acontece até um minuto depois.
/// </summary>
public static class ContractNotificationMessageBuilder
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Build(ContractNotificationPayload payload)
    {
        var message = new StringBuilder();

        message.AppendLine(Header(payload));
        message.AppendLine();

        message.AppendLine($"{PartnerLabel(payload.DocumentType)}: {CodeAndName(payload.CardCode, payload.CardName)}");

        if (IsChangeList(payload))
            AppendChangeList(message, payload);
        else
            AppendContractBlock(message, payload);

        if (IsPriceFixation(payload.EventType))
            AppendFixationBlock(message, payload);

        message.AppendLine();
        message.Append($"Por {payload.TriggeredBy ?? "-"} em {payload.OccurredAt.ToString("dd/MM/yyyy HH:mm", PtBr)}");

        if (IsClickable(payload.DetailUrl))
        {
            // Sempre a última linha: o WhatsApp monta a prévia a partir da última URL do texto.
            message.AppendLine();
            message.Append(payload.DetailUrl);
        }

        return message.ToString();
    }

    private static string Header(ContractNotificationPayload payload)
    {
        var icon = NotificationEventLabels.EventIcon(payload.EventType);
        var document = NotificationEventLabels.DocumentType(payload.DocumentType).ToUpperInvariant();
        var evento = NotificationEventLabels.Event(payload.EventType).ToUpperInvariant();

        return $"{icon} *{document} {payload.ContractCode} — {evento}*";
    }

    /// <summary>
    /// Na alteração o bloco de dados dá lugar à lista do que mudou. Sem mudanças detectadas
    /// (edição que não tocou campo notificável), volta o bloco normal — melhor repetir dados
    /// conhecidos do que enviar uma mensagem com uma seção vazia.
    /// </summary>
    private static bool IsChangeList(ContractNotificationPayload payload) =>
        payload.EventType == NotificationEventType.HeaderUpdated && payload.FieldChanges.Count > 0;

    private static bool IsPriceFixation(NotificationEventType eventType) =>
        eventType is NotificationEventType.PriceFixationCreated
            or NotificationEventType.PriceFixationApproved
            or NotificationEventType.PriceFixationRejected
            or NotificationEventType.PriceFixationReversed;

    private static void AppendChangeList(StringBuilder message, ContractNotificationPayload payload)
    {
        message.AppendLine();
        message.AppendLine("*Alterações:*");

        foreach (var change in payload.FieldChanges)
            message.AppendLine(
                $"• {change.Label}: {ChangeValue(payload, change, change.OldNumber, change.OldValue)}"
                + $" → {ChangeValue(payload, change, change.NewNumber, change.NewValue)}");
    }

    /// <summary>
    /// Volume e preço do contrato saem na unidade comercial, como no resto da mensagem. O custo de
    /// frete fica de fora: tem unidade própria (<c>FreightUmCode</c>). Sem o número cru (payload
    /// gravado antes desta mudança) usa o texto já formatado.
    /// </summary>
    private static string? ChangeValue(
        ContractNotificationPayload payload,
        ContractNotificationFieldChange change,
        decimal? number,
        string? text)
    {
        if (number is not { } value || !HasCommercialUnit(payload))
            return text;

        return change.Field switch
        {
            "TotalVolume" or "Volume" => Volume(payload, value),
            "StandardPrice" or "Price" =>
                $"{(value * payload.CommercialFactor!.Value).ToString("N2", PtBr)} / {payload.CommercialUnitOfMeasureCode}",
            _ => text,
        };
    }

    private static void AppendContractBlock(StringBuilder message, ContractNotificationPayload payload)
    {
        message.AppendLine($"Produto: {CodeAndName(payload.ItemCode, payload.ItemName)}");
        message.AppendLine($"Volume: {Volume(payload, payload.TotalVolume)}");

        // Contrato a fixar (PAF) nasce sem preço — mostrar "0,00" sugeriria um valor acordado
        // que não existe.
        if (payload.Price > 0)
            message.AppendLine($"Preço: {Price(payload, payload.Price)}");

        if (payload.DeliveryStartDate.HasValue || payload.DeliveryEndDate.HasValue)
            message.AppendLine($"Entrega: {Date(payload.DeliveryStartDate)} a {Date(payload.DeliveryEndDate)}");

        if (!string.IsNullOrWhiteSpace(payload.DeliveryLocationName))
            message.AppendLine($"Local de entrega: {payload.DeliveryLocationName}");

        if (!string.IsNullOrWhiteSpace(payload.HarvestSeasonCode))
            message.AppendLine($"Safra: {payload.HarvestSeasonCode}");

        if (!string.IsNullOrWhiteSpace(payload.BranchName))
            message.AppendLine($"Filial: {payload.BranchName}");
        else if (!string.IsNullOrWhiteSpace(payload.BranchCode))
            message.AppendLine($"Filial: {payload.BranchCode}");

        if (!string.IsNullOrWhiteSpace(payload.StatusLabel))
            message.AppendLine($"Situação: {payload.StatusLabel}");
    }

    private static void AppendFixationBlock(StringBuilder message, ContractNotificationPayload payload)
    {
        message.AppendLine();
        message.AppendLine("*Fixação:*");
        message.AppendLine($"Volume: {Volume(payload, payload.FixationVolume ?? 0)}");
        message.AppendLine($"Preço: {Price(payload, payload.FixationPrice ?? 0)}");

        if (!string.IsNullOrWhiteSpace(payload.FixationStatusLabel))
            message.AppendLine($"Situação: {payload.FixationStatusLabel}");
    }

    private static string PartnerLabel(NotificationDocumentType documentType) =>
        documentType == NotificationDocumentType.PurchaseContract ? "Fornecedor" : "Cliente";

    private static string CodeAndName(string? code, string? name) =>
        string.IsNullOrWhiteSpace(name) ? code ?? "-" : $"{code} - {name}";

    /// <summary>
    /// Mesmo cadastro que o diálogo de faturamento (<c>ITEM_COMPLEMENTS</c>). O snapshot só traz
    /// UM e fator quando a conversão é válida; payload antigo não traz nenhum dos dois.
    /// </summary>
    private static bool HasCommercialUnit(ContractNotificationPayload payload) =>
        !string.IsNullOrWhiteSpace(payload.CommercialUnitOfMeasureCode) && payload.CommercialFactor > 0;

    /// <summary>
    /// Na unidade comercial com o KG entre parênteses — o KG é a verdade física do contrato.
    /// Volume comercial = KG / fator.
    /// </summary>
    private static string Volume(ContractNotificationPayload payload, decimal volume)
    {
        var physical = PhysicalVolume(volume, payload.UnitOfMeasureCode);

        if (!HasCommercialUnit(payload))
            return physical;

        var commercial = volume / payload.CommercialFactor!.Value;
        return $"{commercial.ToString("N3", PtBr)} {payload.CommercialUnitOfMeasureCode} ({physical})";
    }

    /// <summary>Preço comercial = preço por KG × fator — a mesma conta do faturamento.</summary>
    private static string Price(ContractNotificationPayload payload, decimal price) =>
        HasCommercialUnit(payload)
            ? $"{Money(price * payload.CommercialFactor!.Value, payload.CurrencyCode)} / {payload.CommercialUnitOfMeasureCode}"
            : Money(price, payload.CurrencyCode);

    private static string PhysicalVolume(decimal volume, string? unitOfMeasureCode) =>
        $"{volume.ToString("N3", PtBr)} {unitOfMeasureCode}".TrimEnd();

    /// <summary>
    /// O WhatsApp só transforma em link um endereço com nome de domínio. Com IP — caso da
    /// produção Yokotobi, <c>http://192.168.1.144:55000/#/...</c> — ele destaca só o IP, sem porta
    /// nem caminho; localhost e host sem ponto nem isso. Link que não leva à tela é omitido.
    /// Checado na renderização para valer também para o que já está pendente na outbox.
    /// </summary>
    private static bool IsClickable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.HostNameType == UriHostNameType.Dns
        && !uri.IsLoopback
        && uri.Host.Contains('.');

    private static string Money(decimal value, string? currencyCode) =>
        $"{CurrencySymbol(currencyCode)} {value.ToString("N2", PtBr)}";

    private static string CurrencySymbol(string? currencyCode) => currencyCode?.ToUpperInvariant() switch
    {
        "USD" => "US$",
        _ => "R$",
    };

    private static string Date(DateTime? date) => date?.ToString("dd/MM/yyyy", PtBr) ?? "-";
}
