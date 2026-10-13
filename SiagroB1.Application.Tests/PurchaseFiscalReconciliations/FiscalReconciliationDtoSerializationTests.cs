using System.Text.Json;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>
/// As funções da conciliação devolvem <c>IActionResult</c> cru: sem <c>[JsonPropertyName]</c> o ASP.NET serializa em
/// camelCase e o enum como inteiro, e a tela (tipos PascalCase, enum por nome) lê undefined sem erro nenhum.
/// </summary>
public class FiscalReconciliationDtoSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static TheoryData<object> Dtos => new()
    {
        new FiscalReconciliationTransactionDto { Links = [new FiscalReconciliationLinkDto()] },
        new FiscalReconciliationInvoiceLineDto(),
        new FiscalReconciliationLinkDto(),
        new FiscalReconciliationPairDto(),
    };

    [Theory]
    [MemberData(nameof(Dtos))]
    public void Every_property_keeps_its_pascal_case_name(object dto)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, dto.GetType(), Web));
        var expected = dto.GetType().GetProperties().Select(p => p.Name).Order().ToList();

        Assert.Equal(expected, doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
    }

    [Fact]
    public void Enums_are_serialized_by_name()
    {
        var tx = JsonSerializer.Serialize(new FiscalReconciliationTransactionDto
        {
            TransactionType = StorageTransactionType.PurchaseDischargeGain, Status = FiscalReconciliationStatus.Partial,
        }, Web);
        var line = JsonSerializer.Serialize(new FiscalReconciliationInvoiceLineDto { InvoiceType = PurchaseInvoiceType.Return }, Web);

        Assert.Contains("\"TransactionType\":\"PurchaseDischargeGain\"", tx);
        Assert.Contains("\"Status\":\"Partial\"", tx);
        Assert.Contains("\"InvoiceType\":\"Return\"", line);
    }
}
