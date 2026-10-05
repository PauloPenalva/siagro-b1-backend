using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// O que a Zeus precisa para assinar, validar e falar com a SEFAZ de uma filial. O certificado é
/// do chamador (abre e descarta). <paramref name="IssuerState"/> é a sigla da UF do emitente.
/// </summary>
public sealed record NfeServiceSettings(
    NfeEnvironment Environment,
    string IssuerState,
    X509Certificate2 Certificate,
    string SchemasDirectory,
    int TimeoutMilliseconds = 60_000,
    bool ValidateServerCertificate = true)
{
    /// <summary>A pasta <c>Schemas</c> que o <c>SiagroB1.Fiscal</c> copia para a saída de quem o referencia.</summary>
    public static string DefaultSchemasDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas");
}
