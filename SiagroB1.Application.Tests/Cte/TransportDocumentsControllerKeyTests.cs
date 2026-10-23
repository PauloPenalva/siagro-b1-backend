using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Key malformada nunca vira inclusão (senão a edição criaria um CT-e duplicado).</summary>
public class TransportDocumentsControllerKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_key_means_new_document(string? raw)
    {
        Assert.True(TransportDocumentsController.TryParseKey(raw, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void Valid_key_is_parsed_even_url_encoded()
    {
        var g = Guid.NewGuid();
        Assert.True(TransportDocumentsController.TryParseKey(Uri.EscapeDataString(g.ToString()), out var key));
        Assert.Equal(g, key);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1234")]
    public void Malformed_key_is_refused(string raw)
    {
        Assert.False(TransportDocumentsController.TryParseKey(raw, out _));
        Assert.Equal("Chave do documento inválida.", TransportDocumentsController.InvalidKey);
    }
}

public class CteNumbersVoidActionTests
{
    [Fact]
    public async Task Void_without_series_or_with_null_series_is_bad_request()
    {
        var controller = new TransportDocumentsController(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

        foreach (object? series in new object?[] { null, "absent" })
        {
            var parameters = new Microsoft.AspNetCore.OData.Formatter.ODataActionParameters
            {
                ["BranchCode"] = "01", ["From"] = 1, ["To"] = 2, ["Justification"] = "justificativa valida aqui",
            };
            if (!"absent".Equals(series))
                parameters["Series"] = series;

            var result = await controller.VoidNumbersAsync(parameters);

            Assert.IsType<Microsoft.AspNetCore.OData.Results.BadRequestODataResult>(result);
        }
    }
}
