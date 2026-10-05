using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Mecânica e mensagens comuns às travas da NF-e dos dois documentos (saída e entrada).</summary>
public static class NfeLockRules
{
    public const string ProcessingMessage =
        "A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.";

    public const string AuthorizedMessage =
        "A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.";

    public static bool AnyChanged(EntityEntry entry, IEnumerable<string> properties) =>
        properties.Any(p => !Equals(entry.OriginalValues[p], entry.CurrentValues[p]));

    /// <summary>Volta as propriedades ao valor gravado — o "sobrescreve, não recusa".</summary>
    public static void Restore(EntityEntry entry, IEnumerable<string> properties)
    {
        foreach (var property in properties)
            entry.Property(property).CurrentValue = entry.OriginalValues[property];
    }
}
