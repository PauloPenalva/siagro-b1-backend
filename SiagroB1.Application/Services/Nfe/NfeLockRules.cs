using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Mecânica e mensagens comuns às travas da NF-e dos dois documentos (saída e entrada).</summary>
public static class NfeLockRules
{
    public const string ProcessingMessage =
        "A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.";

    public const string AuthorizedMessage =
        "A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.";

    public const string CancelledMessage =
        "A NF-e deste documento foi cancelada na SEFAZ: o documento não pode mudar.";

    public const string VoidedMessage =
        "A numeração da NF-e deste documento foi inutilizada na SEFAZ: o documento não pode mudar.";

    public const string EmittedReverseMessage =
        "Documento com NF-e emitida não pode ser estornado; use o cancelamento.";

    public const string AuthorizedCancelMessage =
        "NF-e autorizada: cancele pela SEFAZ informando a justificativa.";

    public const string CancelledPendingMessage =
        "NF-e já cancelada na SEFAZ: use Concluir cancelamento.";

    /// <summary>Autorizada, cancelada ou inutilizada: o que foi para a nota não muda mais.</summary>
    public static bool IsFrozen(NfeStatus status) =>
        status is NfeStatus.Authorized or NfeStatus.Cancelled or NfeStatus.Voided;

    public static string FrozenMessage(NfeStatus status) => status switch
    {
        NfeStatus.Cancelled => CancelledMessage,
        NfeStatus.Voided => VoidedMessage,
        _ => AuthorizedMessage,
    };

    public static bool AnyChanged(EntityEntry entry, IEnumerable<string> properties) =>
        properties.Any(p => !Equals(entry.OriginalValues[p], entry.CurrentValues[p]));

    /// <summary>Volta as propriedades ao valor gravado — o "sobrescreve, não recusa".</summary>
    public static void Restore(EntityEntry entry, IEnumerable<string> properties)
    {
        foreach (var property in properties)
            entry.Property(property).CurrentValue = entry.OriginalValues[property];
    }
}
