using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SiagroB1.Installer.Core.Settings;

public sealed class InvalidSettingsFileException(string path, string message)
    : Exception($"O arquivo de configuração \"{path}\" é inválido: {message}")
{
    public string FilePath { get; } = path;
}

/// <summary>Leitura e gravação de appsettings. Aceita comentários e vírgula final, como o ASP.NET Core.</summary>
public static class JsonFile
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Mantém acentos legíveis no arquivo do cliente (sem é).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonObject Load(string path)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions);
            return node as JsonObject
                   ?? throw new InvalidSettingsFileException(path, "o conteúdo não é um objeto JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidSettingsFileException(path, ex.Message);
        }
    }

    public static void Save(string path, JsonObject obj)
    {
        File.WriteAllText(path, obj.ToJsonString(WriteOptions), new UTF8Encoding(false));
    }
}
