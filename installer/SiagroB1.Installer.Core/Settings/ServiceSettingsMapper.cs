using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;

namespace SiagroB1.Installer.Core.Settings;

/// <summary>Traduz as respostas do assistente para as chaves de cada serviço, e o caminho inverso.</summary>
public static class ServiceSettingsMapper
{
    private const string BackendAddress = "ReverseProxy:Clusters:backend:Destinations:odata-destination:Address";
    private const string ReportsAddress = "ReverseProxy:Clusters:reports:Destinations:reports-destination:Address";

    // Seção de topo da página Integrações → serviço que lê a chave.
    private static readonly Dictionary<string, ServiceKind> IntegrationOwner = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Email"] = ServiceKind.Gateway,
        ["Security"] = ServiceKind.Gateway,
        ["Notifications"] = ServiceKind.Web,
        ["Signature"] = ServiceKind.Web,
        ["Nfe"] = ServiceKind.Web,
        ["Reinf"] = ServiceKind.Web,
        ["Averba"] = ServiceKind.Web
    };

    public static IReadOnlyCollection<string> DropPaths(ServiceKind kind) =>
        kind == ServiceKind.Gateway ? ["ReverseProxy:Routes"] : [];

    public static IReadOnlyDictionary<string, JsonNode?> ToEdits(ServiceKind kind, InstallSettings s)
    {
        var edits = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:SiagroDB"] = ConnectionString(s, "SiagroDB", s.CompanyDatabase),
            ["ConnectionStrings:SiagroCommon"] = ConnectionString(s, "SiagroCommon", s.CommonDatabase),
            ["Erp"] = s.IsSapB1 ? "SAPB1" : "STANDALONE",
            ["CompanyName"] = s.CompanyName
        };

        if (s.IsSapB1)
        {
            edits["ConnectionStrings:SapDB"] = ConnectionString(s, "SapDB", s.SapDatabase);
            edits["ConnectionStrings:SapCommon"] = ConnectionString(s, "SapCommon", s.SapCommonDatabase);
        }

        switch (kind)
        {
            case ServiceKind.Web:
                if (s.IsSapB1)
                {
                    edits["SAPB1:ServiceLayerUrl"] = s.SapServiceLayerUrl;
                }
                edits["Reports:InternalBaseUrl"] = $"http://localhost:{s.ReportsPort}";
                edits["Reports:InternalApiKey"] = s.ReportsInternalApiKey;
                edits["Notifications:AppBaseUrl"] = s.PublicBaseUrl;
                break;
            case ServiceKind.Gateway:
                edits["Security:AppBaseUrl"] = s.PublicBaseUrl;
                edits[BackendAddress] = $"http://localhost:{s.WebPort}/";
                edits[ReportsAddress] = $"http://localhost:{s.ReportsPort}/";
                break;
            case ServiceKind.Reports:
                edits["Reports:InternalApiKey"] = s.ReportsInternalApiKey;
                edits["CompanyLogoPath"] = "wwwroot/images/logo.png";
                break;
        }

        foreach (var (path, value) in s.Integrations)
        {
            var section = path.Split(':')[0];
            var owner = IntegrationOwner.GetValueOrDefault(section, ServiceKind.Web);
            if (owner == kind)
            {
                edits[path] = value?.DeepClone();
            }
        }

        return edits;
    }

    public static InstallSettings FromClientFiles(JsonObject? web, JsonObject? gateway, JsonObject? reports)
    {
        var s = new InstallSettings();
        var files = new[] { web, gateway, reports }.Where(f => f is not null).Cast<JsonObject>().ToList();

        string? First(string path) => files.Select(f => Read(f, path)).FirstOrDefault(v => !string.IsNullOrEmpty(v));

        foreach (var name in new[] { "SiagroDB", "SiagroCommon", "SapDB", "SapCommon" })
        {
            if (First($"ConnectionStrings:{name}") is { } cs)
            {
                s.OriginalConnectionStrings[name] = cs;
            }
        }

        var common = Parse(s.OriginalConnectionStrings.GetValueOrDefault("SiagroCommon"))
                     ?? Parse(s.OriginalConnectionStrings.GetValueOrDefault("SiagroDB"));
        if (common is not null)
        {
            s.SqlServer = common.DataSource;
            s.UseWindowsAuth = common.IntegratedSecurity;
            s.SqlUser = common.UserID;
            s.SqlPassword = common.Password;
        }

        s.CommonDatabase = Parse(s.OriginalConnectionStrings.GetValueOrDefault("SiagroCommon"))?.InitialCatalog ?? "";
        s.CompanyDatabase = Parse(s.OriginalConnectionStrings.GetValueOrDefault("SiagroDB"))?.InitialCatalog ?? "";
        s.SapDatabase = Parse(s.OriginalConnectionStrings.GetValueOrDefault("SapDB"))?.InitialCatalog ?? "";
        s.SapCommonDatabase = Parse(s.OriginalConnectionStrings.GetValueOrDefault("SapCommon"))?.InitialCatalog ?? "";

        s.ErpMode = string.Equals(First("Erp"), "SAPB1", StringComparison.OrdinalIgnoreCase) ? "SAPB1" : "STANDALONE";
        s.SapServiceLayerUrl = First("SAPB1:ServiceLayerUrl") ?? "";
        s.CompanyName = First("CompanyName") ?? "";
        s.PublicBaseUrl = (gateway is null ? null : Read(gateway, "Security:AppBaseUrl"))
                          ?? First("Notifications:AppBaseUrl") ?? "";
        s.ReportsInternalApiKey = First("Reports:InternalApiKey") ?? "";

        if (gateway is not null)
        {
            s.WebPort = PortOf(Read(gateway, BackendAddress)) ?? s.WebPort;
            s.ReportsPort = PortOf(Read(gateway, ReportsAddress)) ?? s.ReportsPort;
        }

        return s;
    }

    private static string ConnectionString(InstallSettings s, string name, string database)
    {
        var original = s.OriginalConnectionStrings.GetValueOrDefault(name);
        var builder = Parse(original) ?? new SqlConnectionStringBuilder { TrustServerCertificate = true };

        // Só atribui o que mudou: atribuir um valor igual ainda acrescenta a chave na string e
        // quebraria a comparação com o original.
        void SetIfDifferent<T>(T current, T desired, Action<T> set)
        {
            if (!EqualityComparer<T>.Default.Equals(current, desired))
            {
                set(desired);
            }
        }

        SetIfDifferent(builder.DataSource, s.SqlServer, v => builder.DataSource = v);
        SetIfDifferent(builder.InitialCatalog, database, v => builder.InitialCatalog = v);
        SetIfDifferent(builder.IntegratedSecurity, s.UseWindowsAuth, v => builder.IntegratedSecurity = v);
        if (s.UseWindowsAuth)
        {
            builder.Remove("User ID");
            builder.Remove("Password");
        }
        else
        {
            SetIfDifferent(builder.UserID, s.SqlUser, v => builder.UserID = v);
            SetIfDifferent(builder.Password, s.SqlPassword, v => builder.Password = v);
        }

        if (original is not null && Parse(original) is { } before && before.EquivalentTo(builder))
        {
            return original;
        }
        return builder.ConnectionString;
    }

    private static SqlConnectionStringBuilder? Parse(string? cs)
    {
        if (string.IsNullOrWhiteSpace(cs))
        {
            return null;
        }
        try
        {
            return new SqlConnectionStringBuilder(cs);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static int? PortOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Port : null;

    private static string? Read(JsonObject root, string path)
    {
        JsonNode? current = root;
        foreach (var segment in path.Split(':'))
        {
            if (current is not JsonObject obj)
            {
                return null;
            }
            var key = obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, segment, StringComparison.OrdinalIgnoreCase));
            if (key is null)
            {
                return null;
            }
            current = obj[key];
        }
        return current is JsonValue value && value.TryGetValue<string>(out var text) ? text : current?.ToString();
    }
}
