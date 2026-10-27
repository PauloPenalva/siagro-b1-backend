using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Settings;

public class AppSettingsMergerTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static readonly IReadOnlyDictionary<string, JsonNode?> NoEdits = new Dictionary<string, JsonNode?>();
    private static readonly IReadOnlyCollection<string> NoDrops = Array.Empty<string>();

    [Fact]
    public void Merge_NewInstall_ReturnsOnlyEdits()
    {
        var edits = new Dictionary<string, JsonNode?> { ["Erp"] = "SAPB1" };

        var result = AppSettingsMerger.Merge(Obj("""{"Erp":"STANDALONE","Nfe":{"TimeoutSeconds":60}}"""), null, edits, NoDrops);

        Assert.True(JsonNode.DeepEquals(Obj("""{"Erp":"SAPB1"}"""), result.Result));
        Assert.Empty(result.NewKeys);
    }

    [Fact]
    public void Merge_KeepsClientValue()
    {
        var result = AppSettingsMerger.Merge(Obj("""{"Email":{"Port":587}}"""), Obj("""{"Email":{"Port":2525}}"""), NoEdits, NoDrops);

        Assert.Equal(2525, result.Result["Email"]!["Port"]!.GetValue<int>());
    }

    [Fact]
    public void Merge_ReportsNewKeyWithoutCopying()
    {
        var result = AppSettingsMerger.Merge(
            Obj("""{"Erp":"SAPB1","Nfe":{"TimeoutSeconds":60}}"""),
            Obj("""{"Erp":"SAPB1"}"""), NoEdits, NoDrops);

        Assert.Contains("Nfe:TimeoutSeconds", result.NewKeys);
        Assert.Null(result.Result["Nfe"]);
    }

    [Fact]
    public void Merge_KeepsAndReportsObsoleteKey()
    {
        var result = AppSettingsMerger.Merge(Obj("""{"Erp":"SAPB1"}"""), Obj("""{"Old":{"X":1}}"""), NoEdits, NoDrops);

        Assert.Equal(1, result.Result["Old"]!["X"]!.GetValue<int>());
        Assert.Contains("Old:X", result.ObsoleteKeys);
    }

    [Fact]
    public void Merge_EditOverridesClient()
    {
        var edits = new Dictionary<string, JsonNode?> { ["Email:Host"] = "smtp.novo" };

        var result = AppSettingsMerger.Merge(Obj("""{"Email":{"Host":""}}"""), Obj("""{"email":{"host":"smtp.velho"}}"""), edits, NoDrops);

        Assert.Equal("smtp.novo", result.Result["email"]!["host"]!.GetValue<string>());
        Assert.Contains("Email:Host", result.ChangedKeys);
    }

    [Fact]
    public void Merge_EditEqualToClient_IsNotChanged()
    {
        var edits = new Dictionary<string, JsonNode?> { ["Erp"] = "SAPB1" };

        var result = AppSettingsMerger.Merge(Obj("{}"), Obj("""{"Erp":"SAPB1"}"""), edits, NoDrops);

        Assert.Empty(result.ChangedKeys);
    }

    [Fact]
    public void Merge_ArrayIsAtomic()
    {
        var result = AppSettingsMerger.Merge(Obj("""{"A":[1,2]}"""), Obj("""{"A":[3]}"""), NoEdits, NoDrops);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[3]"), result.Result["A"]));
        Assert.DoesNotContain(result.NewKeys, k => k.StartsWith("A"));
    }

    [Fact]
    public void Merge_DropPathRemovesSubtree()
    {
        var client = Obj("""{"ReverseProxy":{"Routes":{"x":{"ClusterId":"b"}},"Clusters":{"b":{"Destinations":{"d":{"Address":"http://localhost:55100/"}}}}}}""");

        var result = AppSettingsMerger.Merge(Obj("{}"), client, NoEdits, new[] { "ReverseProxy:Routes" });

        Assert.Null(result.Result["ReverseProxy"]!["Routes"]);
        Assert.NotNull(result.Result["ReverseProxy"]!["Clusters"]);
    }

    [Fact]
    public void Merge_DroppedPathIsNotReportedAsNew()
    {
        var defaults = Obj("""{"ReverseProxy":{"Routes":{"odata":{"ClusterId":"backend"}}}}""");

        var result = AppSettingsMerger.Merge(defaults, Obj("""{"Erp":"SAPB1"}"""), NoEdits, new[] { "ReverseProxy:Routes" });

        Assert.Empty(result.NewKeys);
    }

    [Fact]
    public void Merge_EditNullRemovesKey()
    {
        var edits = new Dictionary<string, JsonNode?> { ["ConnectionStrings:SapDB"] = null };

        var result = AppSettingsMerger.Merge(Obj("{}"), Obj("""{"ConnectionStrings":{"SapDB":"x","SiagroDB":"y"}}"""), edits, NoDrops);

        Assert.Null(result.Result["ConnectionStrings"]!["SapDB"]);
        Assert.NotNull(result.Result["ConnectionStrings"]!["SiagroDB"]);
    }

    [Fact]
    public void Merge_DoesNotMutateInputs()
    {
        var client = Obj("""{"Erp":"SAPB1"}""");
        var edits = new Dictionary<string, JsonNode?> { ["Erp"] = "STANDALONE" };

        AppSettingsMerger.Merge(Obj("{}"), client, edits, NoDrops);

        Assert.Equal("SAPB1", client["Erp"]!.GetValue<string>());
    }

    [Fact]
    public void Load_AcceptsCommentsAndTrailingComma()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{\n // comentário\n \"Erp\": \"SAPB1\",\n}");

        var obj = JsonFile.Load(path);

        Assert.Equal("SAPB1", obj["Erp"]!.GetValue<string>());
    }

    [Fact]
    public void Load_InvalidJson_ThrowsInvalidSettingsFileException()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "{ \"Erp\": ");

        var ex = Assert.Throws<InvalidSettingsFileException>(() => JsonFile.Load(path));

        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Save_RoundTrips()
    {
        var path = Path.GetTempFileName();
        JsonFile.Save(path, Obj("""{"Nome":"Comércio"}"""));

        Assert.Equal("Comércio", JsonFile.Load(path)["Nome"]!.GetValue<string>());
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
    }
}
