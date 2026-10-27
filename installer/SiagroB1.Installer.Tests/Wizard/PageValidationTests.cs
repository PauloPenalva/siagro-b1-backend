using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Detection;
using SiagroB1.Installer.Core.Migrations;
using SiagroB1.Installer.Core.Settings;
using SiagroB1.Installer.Wizard;

namespace SiagroB1.Installer.Tests.Wizard;

public class PageValidationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("siagro-wizard").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class FakeQuery(params (string, string)[] services) : IServiceQuery
    {
        public IEnumerable<(string Name, string BinPath)> All() => services;
    }

    private WizardServices Services(Func<int, bool>? inUse = null, string? connectionError = null, IServiceQuery? query = null) =>
        new(
            (_, _) => Task.FromResult(connectionError),
            inUse ?? (_ => false),
            query ?? new FakeQuery(),
            null!,
            null!);

    private WizardState State() => new(new PayloadInfo(Path.Combine(_root, "payload"), "1.1.300")) { BasePath = Path.Combine(_root, "inst") };

    private static void FillDatabase(InstallSettings s)
    {
        s.SqlServer = "srv";
        s.SqlUser = "idx";
        s.SqlPassword = "segredo";
        s.CommonDatabase = "COMMON";
        s.CompanyDatabase = "EMP";
    }

    [Fact]
    public void Database_RequiresServerAndCommon()
    {
        var vm = new DatabasePageViewModel(State(), Services());

        var errors = vm.Validate();

        Assert.Contains("Informe o servidor SQL.", errors);
        Assert.Contains("Informe o banco Common.", errors);
        Assert.Contains("Informe o banco da empresa.", errors);
    }

    [Fact]
    public async Task Database_TestConnection_RequiredBeforeNext()
    {
        var state = State();
        FillDatabase(state.Settings);
        var vm = new DatabasePageViewModel(state, Services());

        Assert.Contains("Clique em \"Testar conexão\" antes de avançar.", vm.Validate());

        await vm.TestConnectionAsync();
        Assert.Empty(vm.Validate());
        Assert.Equal("Conexão bem-sucedida.", vm.TestResult);

        vm.SqlServer = "outro";
        Assert.Contains("Clique em \"Testar conexão\" antes de avançar.", vm.Validate());
    }

    [Fact]
    public async Task Database_TestConnection_ShowsSqlError()
    {
        var state = State();
        FillDatabase(state.Settings);
        var vm = new DatabasePageViewModel(state, Services(connectionError: "Login failed for user 'idx'."));

        await vm.TestConnectionAsync();

        Assert.Contains("Login failed", vm.TestResult);
        Assert.NotEmpty(vm.Validate());
    }

    [Fact]
    public void Erp_Sapb1_RequiresSapDatabasesAndServiceLayerUrl()
    {
        var state = State();
        var vm = new ErpPageViewModel(state, Services()) { IsSapB1 = true };

        var errors = vm.Validate();

        Assert.Contains("Informe o banco da empresa no SAP.", errors);
        Assert.Contains("Informe o banco SBO-COMMON.", errors);
        Assert.Contains("Informe a URL do Service Layer.", errors);

        vm.IsSapB1 = false;
        Assert.Empty(vm.Validate());
        Assert.Equal("STANDALONE", state.Settings.ErpMode);
    }

    [Fact]
    public void CompanyPorts_UsesPortChecker()
    {
        var state = State();
        state.Settings.CompanyName = "ACME";
        state.Settings.PublicBaseUrl = "http://srv:55000";
        var vm = new CompanyPortsPageViewModel(state, Services(inUse: p => p == 55000));

        Assert.Contains("A porta 55000 já está em uso por outro programa.", vm.Validate());

        state.Detection = new DetectionResult(null,
            [new InstalledService(ServiceKind.Gateway, "gw", @"C:\x\SiagroB1.Gateway.exe", "Production", 55000)], true);
        Assert.Empty(vm.Validate());
    }

    [Fact]
    public void CompanyPorts_RequiresNameAndValidUrl()
    {
        var state = State();
        state.Settings.PublicBaseUrl = "srv:55000";
        var vm = new CompanyPortsPageViewModel(state, Services());

        var errors = vm.Validate();

        Assert.Contains("Informe o nome da empresa.", errors);
        Assert.Contains("Informe a URL de acesso completa, por exemplo http://servidor:55000.", errors);
    }

    [Fact]
    public void Folder_Leave_DetectsLegacyAndPrefills()
    {
        var inst = Path.Combine(_root, "inst");
        Directory.CreateDirectory(Path.Combine(inst, "Web"));
        Directory.CreateDirectory(Path.Combine(inst, "Gateway"));
        File.Copy(Path.Combine("Fixtures", "yokotobi-web.Production.json"), Path.Combine(inst, "Web", "appsettings.Production.json"));
        File.Copy(Path.Combine("Fixtures", "yokotobi-gateway.Production.json"), Path.Combine(inst, "Gateway", "appsettings.Production.json"));
        var query = new FakeQuery(
            ("siagrob1webservice", $"\"{Path.Combine(inst, "Web", "SiagroB1.Web.exe")}\" --environment Production --urls http://*:55100"),
            ("siagrob1gatewayservice", $"\"{Path.Combine(inst, "Gateway", "SiagroB1.Gateway.exe")}\" --environment Production --urls http://*:55000"));
        var state = State();
        var vm = new FolderPageViewModel(state, Services(query: query));

        vm.OnLeave();

        Assert.True(state.Detection.IsLegacy);
        Assert.Equal("IDX_SIAGRO_COMMON", state.Settings.CommonDatabase);
        Assert.Equal(55000, state.Settings.GatewayPort);
        Assert.Equal("SAPB1", state.Settings.ErpMode);
        Assert.NotNull(state.ClientFiles[ServiceKind.Web]);
    }

    [Fact]
    public void Folder_Leave_InvalidClientFile_ShowsError()
    {
        var inst = Path.Combine(_root, "inst");
        Directory.CreateDirectory(Path.Combine(inst, "Web"));
        File.WriteAllText(Path.Combine(inst, "Web", "appsettings.Production.json"), "{ quebrado");
        var query = new FakeQuery(("w", $"\"{Path.Combine(inst, "Web", "SiagroB1.Web.exe")}\" --urls http://*:55100"));
        var vm = new FolderPageViewModel(State(), Services(query: query));

        vm.OnLeave();

        Assert.Contains(vm.Validate(), e => e.Contains("appsettings.Production.json") && e.Contains("inválido"));
    }

    [Fact]
    public void NewKeys_HiddenWhenNoNewKeys()
    {
        var state = State();
        var vm = new NewKeysPageViewModel(state, Services());

        vm.OnEnter();

        Assert.False(vm.IsVisible());
    }

    [Fact]
    public void NewKeys_ListsKeysMissingFromClientAndSavesEdits()
    {
        var state = State();
        var payloadWeb = Path.Combine(_root, "payload", "Web");
        Directory.CreateDirectory(payloadWeb);
        File.WriteAllText(Path.Combine(payloadWeb, "appsettings.json"), """{"Erp":"","Nfe":{"TimeoutSeconds":60}}""");
        state.ClientFiles[ServiceKind.Web] = JsonNode.Parse("""{"Erp":"SAPB1"}""")!.AsObject();
        var vm = new NewKeysPageViewModel(state, Services());

        vm.OnEnter();
        var item = Assert.Single(vm.Items);
        Assert.Equal("Nfe:TimeoutSeconds", item.Key);
        Assert.Equal("60", item.Value);
        item.Value = "90";
        vm.OnLeave();

        Assert.True(vm.IsVisible());
        Assert.Equal(90, state.Settings.ServiceEdits[ServiceKind.Web]["Nfe:TimeoutSeconds"]!.GetValue<int>());
    }

    [Fact]
    public void Integrations_OnlyChangedFieldsAreSaved()
    {
        var state = State();
        state.ClientFiles[ServiceKind.Gateway] = JsonNode.Parse("""{"Email":{"Host":"smtp.velho","Port":587}}""")!.AsObject();
        var vm = new IntegrationsPageViewModel(state, Services());

        vm.OnEnter();
        Assert.Equal("smtp.velho", vm.Fields.Single(f => f.Path == "Email:Host").Value);
        vm.Fields.Single(f => f.Path == "Email:Port").Value = "2525";
        vm.OnLeave();

        Assert.Equal(2525, state.Settings.Integrations["Email:Port"]!.GetValue<int>());
        Assert.DoesNotContain("Email:Host", state.Settings.Integrations.Keys);
    }

    [Fact]
    public void Summary_RequiresDbBackupCheckboxWhenPendingMigrations()
    {
        var state = State();
        state.MigrationReport = new MigrationReport([new PendingMigrations("Common", ["M1"])]);
        var vm = new SummaryPageViewModel(state, Services());

        Assert.Contains("Confirme que foi feito o backup do banco de dados.", vm.Validate());

        vm.DatabaseBackupConfirmed = true;
        Assert.Empty(vm.Validate());
        Assert.True(state.DatabaseBackupConfirmed);
    }

    [Fact]
    public void Summary_MasksSecrets()
    {
        var state = State();
        FillDatabase(state.Settings);
        state.Settings.ReportsInternalApiKey = "chave-super-secreta";
        var vm = new SummaryPageViewModel(state, Services());

        vm.BuildLines();

        Assert.NotEmpty(vm.Lines);
        Assert.DoesNotContain(vm.Lines, l => l.Contains("segredo") || l.Contains("chave-super-secreta"));
    }
}
