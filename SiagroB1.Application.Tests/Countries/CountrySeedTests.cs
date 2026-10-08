using SiagroB1.Migrations.Seeds;

namespace SiagroB1.Application.Tests.Countries;

/// <summary>
/// Países da tabela do BACEN (a do <c>cPais</c>/<c>xPais</c> da NF-e), chaveados pelo ISO-2 que o
/// endereço do parceiro já guarda (<c>BR</c>). ISO-2 ou BACEN repetido quebra a PK/índice único
/// só quando a migration roda no banco — o InMemory não acusa.
/// </summary>
public class CountrySeedTests
{
    [Fact]
    public void Seed_has_unique_iso_and_bacen_codes()
    {
        var rows = CountrySeed.Read();

        Assert.True(rows.Count >= 190, $"só {rows.Count} países");
        Assert.All(rows, r => Assert.Matches("^[A-Z]{2}$", r.Code));
        Assert.All(rows, r => Assert.Matches("^[0-9]{4}$", r.BacenCode));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.Equal(rows.Count, rows.Select(r => r.Code).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.BacenCode).Distinct().Count());
    }

    [Fact]
    public void Seed_has_brazil_with_the_nfe_code()
    {
        var rows = CountrySeed.Read();

        Assert.Contains(rows, r => r is { Code: "BR", BacenCode: "1058", Name: "BRASIL" });
    }

    [Fact]
    public void Insert_batches_cover_every_row()
    {
        var rows = CountrySeed.Read();
        var batches = CountrySeed.InsertBatches(100).ToList();

        Assert.Equal((rows.Count + 99) / 100, batches.Count);
        Assert.All(batches, b => Assert.StartsWith("INSERT INTO COUNTRIES (Code, BacenCode, Name) VALUES", b));
        Assert.Contains(batches, b => b.Contains("('BR', '1058', N'BRASIL')"));
    }
}
