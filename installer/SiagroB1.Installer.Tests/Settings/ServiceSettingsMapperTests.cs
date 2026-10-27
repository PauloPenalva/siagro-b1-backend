using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Settings;

public class ServiceSettingsMapperTests
{
    private static InstallSettings Sapb1() => new()
    {
        SqlServer = "192.168.1.144,1433",
        SqlUser = "idx",
        SqlPassword = "segredo",
        CommonDatabase = "IDX_SIAGRO_COMMON",
        CompanyDatabase = "IDX_SIAGRO_PRD",
        ErpMode = "SAPB1",
        SapDatabase = "SBO_YOKOTOBI_PRD",
        SapCommonDatabase = "SBO-COMMON",
        SapServiceLayerUrl = "https://192.168.1.144:50000/b1s/v2/",
        CompanyName = "COMERCIO DE CEREAIS YOKOTOBI LTDA",
        GatewayPort = 55000,
        WebPort = 55100,
        ReportsPort = 55200,
        PublicBaseUrl = "http://192.168.1.144:55000",
        ReportsInternalApiKey = "chave-interna"
    };

    private static string Str(IReadOnlyDictionary<string, JsonNode?> edits, string key) =>
        edits[key]!.GetValue<string>();

    [Fact]
    public void ToEdits_Web_Sapb1()
    {
        var edits = ServiceSettingsMapper.ToEdits(ServiceKind.Web, Sapb1());

        Assert.Contains("Initial Catalog=IDX_SIAGRO_PRD", Str(edits, "ConnectionStrings:SiagroDB"));
        Assert.Contains("Data Source=192.168.1.144,1433", Str(edits, "ConnectionStrings:SiagroDB"));
        Assert.Contains("Trust Server Certificate=True", Str(edits, "ConnectionStrings:SiagroDB"));
        Assert.Contains("Initial Catalog=IDX_SIAGRO_COMMON", Str(edits, "ConnectionStrings:SiagroCommon"));
        Assert.Contains("Initial Catalog=SBO_YOKOTOBI_PRD", Str(edits, "ConnectionStrings:SapDB"));
        Assert.Contains("Initial Catalog=SBO-COMMON", Str(edits, "ConnectionStrings:SapCommon"));
        Assert.Equal("SAPB1", Str(edits, "Erp"));
        Assert.Equal("COMERCIO DE CEREAIS YOKOTOBI LTDA", Str(edits, "CompanyName"));
        Assert.Equal("https://192.168.1.144:50000/b1s/v2/", Str(edits, "SAPB1:ServiceLayerUrl"));
        Assert.Equal("http://localhost:55200", Str(edits, "Reports:InternalBaseUrl"));
        Assert.Equal("chave-interna", Str(edits, "Reports:InternalApiKey"));
        Assert.Equal("http://192.168.1.144:55000", Str(edits, "Notifications:AppBaseUrl"));
    }

    [Fact]
    public void ToEdits_Web_Standalone_OmitsSapKeys()
    {
        var s = Sapb1();
        s.ErpMode = "STANDALONE";

        var edits = ServiceSettingsMapper.ToEdits(ServiceKind.Web, s);

        Assert.DoesNotContain("ConnectionStrings:SapDB", edits.Keys);
        Assert.DoesNotContain("ConnectionStrings:SapCommon", edits.Keys);
        Assert.DoesNotContain(edits.Keys, k => k.StartsWith("SAPB1:"));
    }

    [Fact]
    public void ToEdits_WindowsAuth_UsesIntegratedSecurity()
    {
        var s = Sapb1();
        s.UseWindowsAuth = true;

        var cs = Str(ServiceSettingsMapper.ToEdits(ServiceKind.Web, s), "ConnectionStrings:SiagroDB");

        Assert.Contains("Integrated Security=True", cs);
        Assert.DoesNotContain("segredo", cs);
    }

    [Fact]
    public void ToEdits_Gateway()
    {
        var edits = ServiceSettingsMapper.ToEdits(ServiceKind.Gateway, Sapb1());

        Assert.Equal("http://192.168.1.144:55000", Str(edits, "Security:AppBaseUrl"));
        Assert.Equal("http://localhost:55100/", Str(edits, "ReverseProxy:Clusters:backend:Destinations:odata-destination:Address"));
        Assert.Equal("http://localhost:55200/", Str(edits, "ReverseProxy:Clusters:reports:Destinations:reports-destination:Address"));
        Assert.Contains("ConnectionStrings:SiagroCommon", edits.Keys);
    }

    [Fact]
    public void ToEdits_Reports()
    {
        var web = ServiceSettingsMapper.ToEdits(ServiceKind.Web, Sapb1());
        var reports = ServiceSettingsMapper.ToEdits(ServiceKind.Reports, Sapb1());

        Assert.Equal(Str(web, "Reports:InternalApiKey"), Str(reports, "Reports:InternalApiKey"));
        Assert.Equal("wwwroot/images/logo.png", Str(reports, "CompanyLogoPath"));
    }

    [Fact]
    public void ToEdits_IncludesIntegrationsForTheirService()
    {
        var s = Sapb1();
        s.Integrations["Email:Host"] = "smtp.x";
        s.Integrations["Signature:Enabled"] = true;

        Assert.Equal("smtp.x", Str(ServiceSettingsMapper.ToEdits(ServiceKind.Gateway, s), "Email:Host"));
        Assert.DoesNotContain("Signature:Enabled", ServiceSettingsMapper.ToEdits(ServiceKind.Gateway, s).Keys);
        Assert.True(ServiceSettingsMapper.ToEdits(ServiceKind.Web, s)["Signature:Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public void DropPaths_OnlyGatewayRoutes()
    {
        Assert.Equal(new[] { "ReverseProxy:Routes" }, ServiceSettingsMapper.DropPaths(ServiceKind.Gateway));
        Assert.Empty(ServiceSettingsMapper.DropPaths(ServiceKind.Web));
        Assert.Empty(ServiceSettingsMapper.DropPaths(ServiceKind.Reports));
    }

    [Fact]
    public void FromClientFiles_Yokotobi()
    {
        var web = JsonFile.Load(Path.Combine("Fixtures", "yokotobi-web.Production.json"));
        var gateway = JsonFile.Load(Path.Combine("Fixtures", "yokotobi-gateway.Production.json"));

        var s = ServiceSettingsMapper.FromClientFiles(web, gateway, null);

        Assert.Equal("192.168.1.144,1433", s.SqlServer);
        Assert.Equal("idx", s.SqlUser);
        Assert.Equal("IDX_SIAGRO_COMMON", s.CommonDatabase);
        Assert.Equal("IDX_SIAGRO_PRD", s.CompanyDatabase);
        Assert.Equal("SAPB1", s.ErpMode);
        Assert.Equal("SBO_YOKOTOBI_PRD", s.SapDatabase);
        Assert.Equal("SBO-COMMON", s.SapCommonDatabase);
        Assert.Equal("https://192.168.1.144:50000/b1s/v2/", s.SapServiceLayerUrl);
        Assert.Equal("COMERCIO DE CEREAIS YOKOTOBI LTDA", s.CompanyName);
        Assert.Equal(55100, s.WebPort);
        Assert.Equal(55200, s.ReportsPort);
        Assert.Equal("http://192.168.1.144:55000", s.PublicBaseUrl);
    }

    [Fact]
    public void FromClientFiles_PrefilledEditsDoNotChangeConnectionStrings()
    {
        var web = JsonFile.Load(Path.Combine("Fixtures", "yokotobi-web.Production.json"));
        var gateway = JsonFile.Load(Path.Combine("Fixtures", "yokotobi-gateway.Production.json"));
        var s = ServiceSettingsMapper.FromClientFiles(web, gateway, null);

        var merged = AppSettingsMerger.Merge(new JsonObject(), web,
            ServiceSettingsMapper.ToEdits(ServiceKind.Web, s), ServiceSettingsMapper.DropPaths(ServiceKind.Web));

        Assert.DoesNotContain(merged.ChangedKeys, k => k.StartsWith("ConnectionStrings:"));
    }

    [Fact]
    public void FromClientFiles_NothingInstalled_ReturnsDefaults()
    {
        var s = ServiceSettingsMapper.FromClientFiles(null, null, null);

        Assert.Equal(55000, s.GatewayPort);
        Assert.Equal(55100, s.WebPort);
        Assert.Equal(55200, s.ReportsPort);
        Assert.Equal("STANDALONE", s.ErpMode);
    }
}
