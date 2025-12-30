using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.DocNumbers;

using System.Data;
using Dapper;

public class DocNumberSequenceService(IDbConnection connection)
{
    public async Task<string> GetDocNumber(Guid docNumberKey)
    {
        var nextNumber = await GetNextNumberAsync(docNumberKey);
        return await FormatNumberAsync(docNumberKey, nextNumber);
    }
    
    public async Task<string> GetDocNumberByObjectCodeAsync(TransactionCode transactionCode)
    {
        var docNumber = await GetNextNumberByObjectNumberAsync(transactionCode);
        return await FormatNumberAsync(docNumber.Key, docNumber.LastNumber);
    }
    
    public async Task<Guid> GetKeyByTransactionCode(TransactionCode transactionCode)
    {
        const string sql = """
                           SELECT [Key]
                           FROM DOC_NUMBERS
                           WHERE TransactionCode = @TransactionCode
                             AND  [Default] = 1;
                           """;

        var key = await connection.ExecuteScalarAsync<Guid?>(
            sql,
            new { TransactionCode = transactionCode });
        
        if (!key.HasValue)
            throw new NotFoundException($"Key {transactionCode} not found.");

        return (Guid)key;
    }
    
    private async Task<DocNumberDocument> GetNextNumberByObjectNumberAsync(TransactionCode transactionCode)
    {
        const string sql = """
                            UPDATE DOC_NUMBERS WITH (UPDLOCK, HOLDLOCK)
                            SET LastNumber = NextNumber,
                               NextNumber = NextNumber + 1
                            OUTPUT inserted.LastNumber, inserted.[Key]
                            WHERE TransactionCode = @TransactionCode
                              AND [Default] = 1;
                           """;

        var docNumber = await connection.QuerySingleOrDefaultAsync<DocNumberDocument?>(
            sql,
            new { TransactionCode = transactionCode }
        );
        
        if (docNumber == null)
            throw new NotFoundException(
                $"No default document number found for transaction {transactionCode}"
            );
        
        return docNumber;
    }
    
    private async Task<int> GetNextNumberAsync(Guid docNumberKey)
    {
        const string sql = """
                            UPDATE DOC_NUMBERS WITH (UPDLOCK, HOLDLOCK)
                            SET LastNumber = NextNumber,
                               NextNumber = NextNumber + 1
                            OUTPUT inserted.LastNumber
                            WHERE [Key] = @Key;
                           """;

        var nextNumber = await connection.ExecuteScalarAsync<int?>(
            sql,
            new { Key = docNumberKey }
        );
        
        if (!nextNumber.HasValue)
            throw new NotFoundException($"Key {docNumberKey} not found.");
        
        return (int) nextNumber;
    }
    
    private async Task<string> FormatNumberAsync(Guid docNumberKey, int currentNumber)
    {
        const string sql = """
                           SELECT Prefix, Suffix, NumberSize
                           FROM DOC_NUMBERS
                           WHERE [Key] = @Key;
                           """;

        var result = await connection
            .QueryFirstOrDefaultAsync<DocNumberFormat>(sql, new { Key = docNumberKey })
            ?? throw new NotFoundException($"Key {docNumberKey} not found.");
        
        var size = Math.Max(1, result.NumberSize);
        
        var formatedNumber = currentNumber
            .ToString()
            .PadLeft(size, '0');

        return $"{result.Prefix}{formatedNumber}{result.Suffix}";

    }
    
    private sealed class DocNumberFormat
    {
        public string Prefix { get; init; } = string.Empty;
        public string Suffix { get; init; } = string.Empty;
        public int NumberSize { get; init; }
    }
    
    private sealed class DocNumberDocument
    {
        public Guid Key { get; init; } = Guid.Empty;
        public int LastNumber { get; init; }
    }
}