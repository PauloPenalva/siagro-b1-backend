using System.Text.Json.Nodes;

namespace SiagroB1.Installer.Core.Settings;

public enum ServiceKind { Web, Gateway, Reports }

/// <summary>Tudo o que o assistente pergunta. Uma instância alimenta os três appsettings.</summary>
public sealed class InstallSettings
{
    public const int DefaultGatewayPort = 55000;
    public const int DefaultWebPort = 55100;
    public const int DefaultReportsPort = 55200;

    public string SqlServer { get; set; } = "";
    public string SqlUser { get; set; } = "";
    public string SqlPassword { get; set; } = "";
    public bool UseWindowsAuth { get; set; }
    public string CommonDatabase { get; set; } = "";
    public string CompanyDatabase { get; set; } = "";

    public string ErpMode { get; set; } = "STANDALONE";
    public string SapDatabase { get; set; } = "";
    public string SapCommonDatabase { get; set; } = "";
    public string SapServiceLayerUrl { get; set; } = "";

    public string CompanyName { get; set; } = "";
    public int GatewayPort { get; set; } = DefaultGatewayPort;
    public int WebPort { get; set; } = DefaultWebPort;
    public int ReportsPort { get; set; } = DefaultReportsPort;
    public string PublicBaseUrl { get; set; } = "";
    public string ReportsInternalApiKey { get; set; } = "";
    public string? LogoSourcePath { get; set; }

    /// <summary>Chaves da página Integrações (caminho completo → valor), só as que o usuário mexeu.</summary>
    public Dictionary<string, JsonNode?> Integrations { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Edições ligadas a um serviço específico (página "Chaves novas").</summary>
    public Dictionary<ServiceKind, Dictionary<string, JsonNode?>> ServiceEdits { get; } = new()
    {
        [ServiceKind.Web] = new(StringComparer.OrdinalIgnoreCase),
        [ServiceKind.Gateway] = new(StringComparer.OrdinalIgnoreCase),
        [ServiceKind.Reports] = new(StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>
    /// Connection strings como estão hoje nos arquivos do cliente (nome → valor). Se o que o usuário
    /// confirmou for equivalente, o texto original é mantido (preserva opções extras e formatação).
    /// </summary>
    public Dictionary<string, string> OriginalConnectionStrings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSapB1 => string.Equals(ErpMode, "SAPB1", StringComparison.OrdinalIgnoreCase);
}
