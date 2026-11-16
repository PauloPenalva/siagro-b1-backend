using Microsoft.Extensions.Configuration;

namespace SiagroB1.Infra;

/// <summary>
/// Leitura única do modo de integração. Mesma regra do <c>Program.cs</c>: chave ausente vale
/// STANDALONE. O teste é POSITIVO de propósito: qualquer modo futuro (PROTHEUS, por exemplo)
/// nasce sem as regras da NF-e STANDALONE.
/// Mora no Infra porque o menu (projeto Security) também pergunta o modo.
/// </summary>
public static class ErpMode
{
    public static bool IsStandalone(IConfiguration configuration) =>
        string.Equals(
            (configuration["Erp"] ?? "STANDALONE").Trim(),
            "STANDALONE",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Modo SAP Business One (Yokotobi). Também POSITIVO: só "SAPB1" liga as regras de vínculo com a invoice do SAP.
    /// </summary>
    public static bool IsSapB1(IConfiguration? configuration) =>
        string.Equals(configuration?["Erp"]?.Trim(), "SAPB1", StringComparison.OrdinalIgnoreCase);
}
