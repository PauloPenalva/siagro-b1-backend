using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Web.Actions.ShipmentLoads;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 — a leitura da data do ticket. Estes casos são de runtime puro: o build passa, o EDM
/// passa, e a data errada só aparece meses depois, no ticket que ninguém conferiu.
/// </summary>
public class ShipmentLoadActionParametersTests
{
    [Fact]
    public void Reads_the_iso_date_the_screen_sends()
    {
        Assert.True(ShipmentLoadActionParameters.TryParseDate("2026-09-18", out var date));
        Assert.Equal(new DateTime(2026, 9, 18), date);
    }

    /// <summary>
    /// ⚠️ O caso mais perigoso da task: com <c>TryParse</c> + InvariantCulture, "05/09/2026"
    /// (5 de setembro no Brasil) parseava como 9 de MAIO e era gravado sem erro nenhum.
    /// </summary>
    [Fact]
    public void Rejects_the_brazilian_date_instead_of_reading_it_as_month_first()
    {
        Assert.False(ShipmentLoadActionParameters.TryParseDate("05/09/2026", out var date));
        Assert.Null(date);
    }

    [Fact]
    public void Rejects_an_unparseable_date()
    {
        Assert.False(ShipmentLoadActionParameters.TryParseDate("18/09/2026", out _));
        Assert.False(ShipmentLoadActionParameters.TryParseDate("amanhã", out _));
    }

    /// <summary>
    /// ISO com sufixo Z virava horário local (UTC-3) antes do <c>.Date</c>, voltando um dia: o
    /// ticket de 18/09 era gravado como 17/09.
    /// </summary>
    [Fact]
    public void Rejects_an_instant_instead_of_shifting_it_a_day_back()
    {
        Assert.False(ShipmentLoadActionParameters.TryParseDate("2026-09-18T00:00:00Z", out _));
    }

    /// <summary>
    /// Ausente não é inválido: quem chama decide. O registro cai no dia de hoje, a alteração
    /// recusa — ver os dois controllers.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reports_an_absent_date_as_absent_not_as_invalid(string? value)
    {
        Assert.True(ShipmentLoadActionParameters.TryParseDate(value, out var date));
        Assert.Null(date);
    }

    [Fact]
    public void Decodes_a_base64_file_and_refuses_a_corrupt_one()
    {
        Assert.True(ShipmentLoadActionParameters.TryDecodeFile("U2lhZ3Jv", out var file));
        Assert.Equal("Siagro"u8.ToArray(), file);

        Assert.False(ShipmentLoadActionParameters.TryDecodeFile("não é base64", out _));
    }

    /// <summary>As mensagens são lidas pelo usuário: pt-BR, sempre.</summary>
    [Fact]
    public void The_messages_are_written_for_the_user()
    {
        Assert.Contains("Data da descarga inválida", ShipmentLoadActionParameters.InvalidDateMessage);
        Assert.Contains("Informe a data da descarga", ShipmentLoadActionParameters.MissingDateMessage);
        Assert.Contains("arquivo anexado", ShipmentLoadActionParameters.UnreadableFileMessage);
    }

    [Fact]
    public void Reads_the_parallel_distribution_arrays()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var parameters = new Dictionary<string, object>
        {
            ["SalesInvoiceItemKeys"] = new List<Guid> { a, b },
            ["Quantities"] = new List<double> { 20000.5, 15000 },
        };

        Assert.True(ShipmentLoadActionParameters.TryReadDistribution(parameters, out var lines, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { new ShipmentLoadDischargeLine(a, 20000.5m), new ShipmentLoadDischargeLine(b, 15000m) }, lines);
    }

    /// <summary>
    /// Review Focus 2: um array de INTEIROS pode chegar como coleção de int/long. O cast direto para
    /// double devolveria lista vazia sem erro, e o ticket seria recusado por "rateio vazio".
    /// </summary>
    [Fact]
    public void Reads_integer_quantities_as_numbers()
    {
        var parameters = new Dictionary<string, object>
        {
            ["SalesInvoiceItemKeys"] = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            ["Quantities"] = new List<object> { 20000, 15000L },
        };

        Assert.True(ShipmentLoadActionParameters.TryReadDistribution(parameters, out var lines, out _));
        Assert.Equal(new[] { 20000m, 15000m }, lines.Select(l => l.Quantity));
    }

    [Fact]
    public void Refuses_arrays_of_different_sizes_and_a_missing_distribution()
    {
        var mismatched = new Dictionary<string, object>
        {
            ["SalesInvoiceItemKeys"] = new List<Guid> { Guid.NewGuid() },
            ["Quantities"] = new List<double> { 1, 2 },
        };

        Assert.False(ShipmentLoadActionParameters.TryReadDistribution(mismatched, out _, out var error));
        Assert.Equal("A lista de itens e a de pesos do rateio têm tamanhos diferentes.", error);

        Assert.False(ShipmentLoadActionParameters.TryReadDistribution(
            new Dictionary<string, object>(), out _, out var missing));
        Assert.Equal(ShipmentLoadActionParameters.MissingDistributionMessage, missing);
    }
}
