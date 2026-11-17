using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Settings;

/// <summary>
/// O padrão que o instalador leva ao servidor é o <c>appsettings.json</c> BASE de cada serviço no
/// repositório (sanitizado pelo build-installer). Chave ou rota que exista só num arquivo de ambiente
/// (<c>appsettings.Yokotobi.json</c>, <c>.Development.json</c>...) funciona na máquina do desenvolvedor e
/// nunca chega ao cliente — foi assim que a rota <c>/api</c> do Gateway faltou na homologação e na
/// produção da Yokotobi. Estes testes leem os arquivos reais do repositório.
/// </summary>
public class RepositoryDefaultsTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SiagroB1.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("SiagroB1.sln não encontrado acima da pasta de testes.");
    }

    /// <summary>O mesmo que vai para o pacote: arquivo base do serviço, sanitizado.</summary>
    private static JsonObject PackagedDefaults(string service) =>
        DefaultsSanitizer.Sanitize(JsonFile.Load(Path.Combine(RepositoryRoot(), $"SiagroB1.{service}", "appsettings.json")));

    private static List<string> GatewayRoutePaths(JsonObject gateway) =>
        gateway["ReverseProxy"]!["Routes"]!.AsObject()
            .Select(route => route.Value!["Match"]!["Path"]!.GetValue<string>())
            .ToList();

    [Theory]
    [InlineData("/odata/{**catch-all}")]
    [InlineData("/api/{**catch-all}")]
    [InlineData("/reports/{**catch-all}")]
    [InlineData("/scales/{**catch-all}")]
    [InlineData("/market/{**catch-all}")]
    [InlineData("/hangfire/{**catch-all}")]
    [InlineData("/hooks/{**catch-all}")]
    [InlineData("/ws/truck-scale")]
    public void Gateway_PackagedDefaultsHaveEveryRouteTheAppCalls(string path)
    {
        Assert.Contains(path, GatewayRoutePaths(PackagedDefaults("Gateway")));
    }

    [Fact]
    public void Gateway_UpdateDropsTheClientRoutesSoThePackagedOnesApply()
    {
        // Arquivo real do servidor da Yokotobi: tem rotas próprias, sem /api.
        var client = JsonFile.Load(Path.Combine("Fixtures", "yokotobi-gateway.Production.json"));
        Assert.DoesNotContain("/api/{**catch-all}", GatewayRoutePaths(client));

        var settings = ServiceSettingsMapper.FromClientFiles(null, client, null);
        var merged = AppSettingsMerger.Merge(
            PackagedDefaults("Gateway"), client,
            ServiceSettingsMapper.ToEdits(ServiceKind.Gateway, settings), ServiceSettingsMapper.DropPaths(ServiceKind.Gateway));

        // Sem "Routes" no arquivo do cliente, vale o padrão do pacote — que tem /api.
        Assert.Null(merged.Result["ReverseProxy"]!["Routes"]);
    }

    [Theory]
    [InlineData("ConnectionStrings:SapFiscalAddonDB")]
    [InlineData("SAPB1:FiscalAddon")]
    public void Web_SapFiscalAddonKeysAreOfferedAsNewOnUpdate(string key)
    {
        // GAC-1226: sem estas chaves no arquivo base, o assistente não as mostra em "Configurações novas
        // desta versão" e o servidor fica sem a chave e sem o filtro de NF autorizada.
        var client = JsonFile.Load(Path.Combine("Fixtures", "yokotobi-web.Production.json"));
        var settings = ServiceSettingsMapper.FromClientFiles(client, null, null);

        var merged = AppSettingsMerger.Merge(
            PackagedDefaults("Web"), client,
            ServiceSettingsMapper.ToEdits(ServiceKind.Web, settings), ServiceSettingsMapper.DropPaths(ServiceKind.Web));

        Assert.Contains(key, merged.NewKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Web")]
    [InlineData("Gateway")]
    [InlineData("Reports")]
    public void PackagedDefaultsCarryNoConnectionString(string service)
    {
        var connections = PackagedDefaults(service)["ConnectionStrings"] as JsonObject;

        Assert.All(connections ?? [], pair => Assert.Equal("", pair.Value!.GetValue<string>()));
    }
}
