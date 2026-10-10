namespace SiagroB1.Application.Services.RoyaltyWriteOffs;

public record RoyaltyWriteOffLineInput(Guid StorageTransactionKey, decimal Weight);

public record RoyaltyWriteOffInput(
    DateTime WriteOffDate,
    string ItsId,
    string TaxId,
    string Name,
    string? Comments,
    IReadOnlyList<RoyaltyWriteOffLineInput> Lines);
