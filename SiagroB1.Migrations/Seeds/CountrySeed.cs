using System.Text;

namespace SiagroB1.Migrations.Seeds;

/// <summary>
/// Países (<c>ISO-2|BACEN|nome</c>), embarcados no assembly das migrations. Nome e código BACEN
/// vêm do <c>ibge/paises.txt</c> do EfisCloud (a tabela do <c>cPais</c>/<c>xPais</c> da NF-e); o
/// ISO-2 foi casado uma vez, ao gerar o arquivo. Lido pela migration <c>CreateCountries</c>.
/// </summary>
public static class CountrySeed
{
    private const string ResourceName = "SiagroB1.Migrations.Seeds.countries.txt";

    public static IReadOnlyList<(string Code, string BacenCode, string Name)> Read()
    {
        using var stream = typeof(CountrySeed).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Recurso {ResourceName} não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var rows = new List<(string Code, string BacenCode, string Name)>();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('|');
            rows.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return rows;
    }

    /// <summary>INSERTs de até <paramref name="batchSize"/> linhas; <c>N'...'</c> e apóstrofo dobrado.</summary>
    public static IEnumerable<string> InsertBatches(int batchSize = 500) =>
        Read()
            .Chunk(batchSize)
            .Select(chunk =>
                "INSERT INTO COUNTRIES (Code, BacenCode, Name) VALUES " +
                string.Join(", ", chunk.Select(r => $"('{r.Code}', '{r.BacenCode}', N'{r.Name.Replace("'", "''")}')")) +
                ";");
}
