using System.Text;

namespace SiagroB1.Migrations.Seeds;

/// <summary>
/// Municípios do IBGE (<c>código|nome|UF</c>), embarcados no assembly das migrations. O arquivo
/// nasceu do <c>cidades.txt</c> + <c>uf.txt</c> do EfisCloud. Lido pela migration
/// <c>CreateMunicipalities</c>: 5.570 <c>InsertData</c> dentro do .cs da migration seriam ilegíveis.
/// </summary>
public static class MunicipalitySeed
{
    private const string ResourceName = "SiagroB1.Migrations.Seeds.municipalities.txt";

    public static IReadOnlyList<(string Code, string Name, string State)> Read()
    {
        using var stream = typeof(MunicipalitySeed).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Recurso {ResourceName} não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var rows = new List<(string Code, string Name, string State)>();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('|');
            rows.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return rows;
    }

    /// <summary>
    /// INSERTs de até <paramref name="batchSize"/> linhas (o SQL Server aceita até 1000 por
    /// <c>VALUES</c>). <c>N'...'</c> preserva os acentos; o apóstrofo é dobrado.
    /// </summary>
    public static IEnumerable<string> InsertBatches(int batchSize = 500) =>
        Read()
            .Chunk(batchSize)
            .Select(chunk =>
                "INSERT INTO MUNICIPALITIES (Code, Name, StateAbbreviation) VALUES " +
                string.Join(", ", chunk.Select(r => $"('{r.Code}', N'{r.Name.Replace("'", "''")}', '{r.State}')")) +
                ";");
}
