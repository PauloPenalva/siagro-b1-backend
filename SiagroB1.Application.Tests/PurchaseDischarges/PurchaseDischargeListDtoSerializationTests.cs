using System.Text.Json;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseDischargeListDtoSerializationTests
{
    [Fact]
    public void Serializes_pascal_case_with_enum_names()
    {
        var json = JsonSerializer.Serialize(new PurchaseDischargeListDto
        {
            TransactionType = StorageTransactionType.PurchaseDischargeGain,
            Status = StorageTransactionsStatus.Confirmed,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"TransactionType\":\"PurchaseDischargeGain\"", json);
        Assert.Contains("\"Status\":\"Confirmed\"", json);
        Assert.Contains("\"PurchaseContractCode\"", json);
    }
}
