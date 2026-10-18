using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.DocNumbers;

/// <summary>Pedido de número de documento. <c>ExistsAsync</c> diz se o código já está em uso na tabela alvo.</summary>
public sealed record DocNumberRequest(
    TransactionCode TransactionCode,
    Guid? DocNumberKey,
    string? TypedCode,
    int MaxLength,
    Func<string, Task<bool>> ExistsAsync);

public sealed record DocNumberAssignment(Guid DocNumberKey, string Code);

public sealed record DocNumberSeries(
    Guid Key,
    TransactionCode TransactionCode,
    string Name,
    bool IsManual,
    bool Inactive);
