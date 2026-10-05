using SiagroB1.Migrations.Seeds;

namespace SiagroB1.Application.Tests.Municipalities;

/// <summary>
/// A semente dos municípios vem do arquivo do IBGE que o EfisCloud já usa em produção. O código
/// do município é o <c>cMun</c> da NF-e e os 2 primeiros dígitos são o <c>cUF</c> — um código
/// errado aqui é rejeição na SEFAZ.
/// </summary>
public class MunicipalitySeedTests
{
    [Fact]
    public void Seed_has_all_5570_municipalities_with_unique_seven_digit_codes()
    {
        var rows = MunicipalitySeed.Read();

        Assert.Equal(5570, rows.Count);
        Assert.All(rows, r => Assert.Matches("^[0-9]{7}$", r.Code));
        Assert.Equal(rows.Count, rows.Select(r => r.Code).Distinct().Count());
    }

    [Fact]
    public void Every_municipality_has_the_state_of_its_ibge_prefix()
    {
        var rows = MunicipalitySeed.Read();

        Assert.Equal(27, rows.Select(r => r.State).Distinct().Count());
        Assert.Contains(rows, r => r is { Code: "3550308", Name: "São Paulo", State: "SP" });
        Assert.Contains(rows, r => r is { Code: "5300108", Name: "Brasília", State: "DF" });
        Assert.Contains(rows, r => r is { Code: "3522406", State: "SP" }); // Itapeva
    }

    [Fact]
    public void Insert_batches_escape_apostrophes_and_cover_every_row()
    {
        var batches = MunicipalitySeed.InsertBatches(500).ToList();

        Assert.Equal(12, batches.Count); // 5570 / 500 = 11,1
        Assert.Contains(batches, b => b.Contains("N'Alta Floresta D''Oeste'"));
        Assert.All(batches, b => Assert.StartsWith("INSERT INTO MUNICIPALITIES (Code, Name, StateAbbreviation) VALUES", b));
    }
}
