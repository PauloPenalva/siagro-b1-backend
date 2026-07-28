using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Dtos.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Notifications;

/// <summary>
/// Monta a lista "campo: de → para" de uma edição de cabeçalho, comparando
/// <c>OriginalValues</c> com <c>CurrentValues</c> da entidade rastreada.
///
/// Puro e estático (recebe o <see cref="EntityEntry"/> pronto) para ser testável sem serviço,
/// sem DI e sem transação — é a lógica com mais casos de borda da feature.
///
/// Só faz sentido chamar ANTES do <c>SaveChanges</c>: depois dele o EF sincroniza
/// <c>OriginalValues</c> com os valores gravados e o diff sai vazio.
/// </summary>
public static class ContractHeaderDiffBuilder
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Marcador de valor ausente — em branco sumiria no meio da mensagem.</summary>
    private const string Empty = "—";

    public static IReadOnlyList<ContractNotificationFieldChange> Build(
        EntityEntry entry, NotificationDocumentType documentType)
    {
        var notifiable = ContractNotifiableFields.For(documentType);
        var changes = new List<ContractNotificationFieldChange>();

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;

            if (!notifiable.Contains(name) || !property.IsModified)
                continue;

            var oldValue = Format(name, property.OriginalValue);
            var newValue = Format(name, property.CurrentValue);

            // IsModified é atribuição, não diferença: o SetValues do UpdateService marca a
            // coluna mesmo quando o valor gravado é igual ao anterior.
            if (oldValue == newValue)
                continue;

            changes.Add(new ContractNotificationFieldChange
            {
                Field = name,
                Label = NotificationEventLabels.Field(documentType, name),
                OldValue = oldValue,
                NewValue = newValue,
            });
        }

        return changes;
    }

    private static string Format(string property, object? value) => value switch
    {
        null => Empty,

        // Volume em 3 casas (é como o contrato o exibe em toda a aplicação), demais decimais
        // em 2 — preço e custo de frete.
        decimal number => number.ToString(
            property.Contains("Volume", StringComparison.Ordinal) ? "N3" : "N2", PtBr),

        DateTime date => date.ToString("dd/MM/yyyy", PtBr),
        bool flag => flag ? "Sim" : "Não",
        string text => string.IsNullOrWhiteSpace(text) ? Empty : text,
        _ => value.ToString() is { Length: > 0 } text ? text : Empty,
    };
}
