using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Settings;

public class DefaultsSanitizerTests
{
    // Mesma forma do SiagroB1.Web/appsettings.json do repositório, com valores falsos.
    private static JsonObject WebDefaults() => JsonNode.Parse("""
        {
          "Logging": { "LogLevel": { "Default": "Information", "Microsoft": "Warning" } },
          "ConnectionStrings": {
            "SiagroDB": "Server=10.0.0.1,1433;Database=X;User Id=idx;Password=falsa;",
            "SapDB": "Server=10.0.0.1;Database=Y;User Id=idx;Password=falsa;"
          },
          "Erp": "SAPB1",
          "Reports": { "InternalBaseUrl": "", "InternalApiKey": "abc" },
          "Email": { "Port": 587, "Password": "falsa", "Host": "smtp.x" },
          "Notifications": { "WhatsApp": { "Token": "t", "ClientToken": "c", "DelayMessageSeconds": 2 } },
          "Signature": { "D4Sign": { "TokenApi": "t", "CryptKey": "k", "WebhookSecret": "s", "BaseUrl": "https://sandbox" } },
          "Security": { "PasswordPolicy": { "MinimumLength": 4 } }
        }
        """)!.AsObject();

    [Fact]
    public void Sanitize_ClearsConnectionStringsAndSecrets()
    {
        var result = DefaultsSanitizer.Sanitize(WebDefaults());

        Assert.Equal("", result["ConnectionStrings"]!["SiagroDB"]!.GetValue<string>());
        Assert.Equal("", result["ConnectionStrings"]!["SapDB"]!.GetValue<string>());
        Assert.Equal("", result["Reports"]!["InternalApiKey"]!.GetValue<string>());
        Assert.Equal("", result["Email"]!["Password"]!.GetValue<string>());
        Assert.Equal("", result["Notifications"]!["WhatsApp"]!["Token"]!.GetValue<string>());
        Assert.Equal("", result["Signature"]!["D4Sign"]!["CryptKey"]!.GetValue<string>());
        Assert.Equal("", result["Signature"]!["D4Sign"]!["WebhookSecret"]!.GetValue<string>());
        Assert.DoesNotContain("falsa", result.ToJsonString());
    }

    [Fact]
    public void Sanitize_KeepsNonSecretValues()
    {
        var result = DefaultsSanitizer.Sanitize(WebDefaults());

        Assert.Equal(587, result["Email"]!["Port"]!.GetValue<int>());
        Assert.Equal("smtp.x", result["Email"]!["Host"]!.GetValue<string>());
        Assert.Equal("SAPB1", result["Erp"]!.GetValue<string>());
        Assert.Equal("https://sandbox", result["Signature"]!["D4Sign"]!["BaseUrl"]!.GetValue<string>());
        Assert.Equal(4, result["Security"]!["PasswordPolicy"]!["MinimumLength"]!.GetValue<int>());
        Assert.Equal(2, result["Notifications"]!["WhatsApp"]!["DelayMessageSeconds"]!.GetValue<int>());
    }

    [Fact]
    public void Sanitize_KeepsLogLevel()
    {
        var result = DefaultsSanitizer.Sanitize(WebDefaults());

        Assert.Equal("Information", result["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>());
    }

    [Fact]
    public void Sanitize_DoesNotMutateInput()
    {
        var input = WebDefaults();

        DefaultsSanitizer.Sanitize(input);

        Assert.Contains("falsa", input.ToJsonString());
    }
}
