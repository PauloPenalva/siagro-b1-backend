using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Confere o <see cref="DocNumberRequest"/> que um serviço de criação entregou ao
/// <see cref="FakeDocNumberSequenceService"/>: transação, série, código digitado, limite do campo e o
/// predicado de duplicidade (verdadeiro para o código de uma linha semeada, falso para um inexistente).
/// </summary>
public static class DocNumberRequestAsserts
{
    public static async Task AssertRequestAsync(
        FakeDocNumberSequenceService fake,
        TransactionCode transactionCode,
        Guid? key,
        string? typedCode,
        int maxLength,
        string seededCode)
    {
        var request = Assert.Single(fake.Requests);

        Assert.Equal(transactionCode, request.TransactionCode);
        Assert.Equal(key, request.DocNumberKey);
        Assert.Equal(typedCode, request.TypedCode);
        Assert.Equal(maxLength, request.MaxLength);
        Assert.True(await request.ExistsAsync(seededCode));
        Assert.False(await request.ExistsAsync("NOPE"));
    }
}
