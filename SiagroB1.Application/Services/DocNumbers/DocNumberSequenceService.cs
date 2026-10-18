using System.Data;
using Dapper;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.DocNumbers;

public class DocNumberSequenceService(IDbConnection connection)
{
    // virtual: a numeração roda em T-SQL puro (Dapper, OUTPUT/UPDLOCK), que o provider
    // InMemory dos testes não traduz. Permite substituí-la por um duplo nos testes de
    // orquestração que só se interessam pelo efeito colateral do fluxo, não pela sequência.
    public virtual async Task<string> GetDocNumber(Guid docNumberKey)
    {
        var nextNumber = await GetNextNumberAsync(docNumberKey);
        return await FormatNumberAsync(docNumberKey, nextNumber);
    }
    
    public async Task<string> GetDocNumberByObjectCodeAsync(TransactionCode transactionCode)
    {
        var docNumber = await GetNextNumberByObjectNumberAsync(transactionCode);
        return await FormatNumberAsync(docNumber.Key, docNumber.LastNumber);
    }
    
    public virtual async Task<Guid> GetKeyByTransactionCode(TransactionCode transactionCode)
    {
        const string sql = """
                           SELECT TOP 1 [Key]
                           FROM DOC_NUMBERS
                           WHERE TransactionCode = @TransactionCode
                             AND [Default] = 1
                             AND IsManual = 0
                             AND Inactive = 0
                           ORDER BY [Key];
                           """;

        var key = await connection.ExecuteScalarAsync<Guid?>(
            sql,
            new { TransactionCode = transactionCode });
        
        if (!key.HasValue)
            throw new NotFoundException($"Key {transactionCode} not found.");

        return (Guid)key;
    }

    public virtual async Task<DocNumberSeries?> GetSeriesAsync(Guid key)
    {
        const string sql = """
                           SELECT [Key], TransactionCode, Name, IsManual, Inactive
                           FROM DOC_NUMBERS
                           WHERE [Key] = @Key;
                           """;

        return await connection.QueryFirstOrDefaultAsync<DocNumberSeries>(sql, new { Key = key });
    }

    // Documentos que nunca aceitam número digitado: o número é sempre gerado pelo sistema.
    public static bool AllowsManual(TransactionCode code) =>
        code is not (TransactionCode.FinancialDocument or TransactionCode.StorageTransaction);

    // Ponto único de atribuição de número: série manual (código digitado) ou automática
    // (contador, saltando códigos já ocupados por digitação manual).
    public virtual async Task<DocNumberAssignment> AssignAsync(DocNumberRequest request)
    {
        var tc = request.TransactionCode;
        var key = request.DocNumberKey ?? await GetKeyByTransactionCode(tc);
        var series = await GetSeriesAsync(key)
            ?? throw new NotFoundException($"Key {key} not found.");

        if (series.TransactionCode != tc)
            throw new ApplicationException("A série de numeração escolhida não pertence a este documento.");

        if (series.Inactive)
            throw new ApplicationException($"A série de numeração \"{series.Name}\" está inativa.");

        if (series.IsManual)
        {
            if (!AllowsManual(tc))
                throw new ApplicationException("Este documento não aceita numeração manual.");

            if (string.IsNullOrWhiteSpace(request.TypedCode))
            {
                // Fluxos gerados pelo sistema herdam a chave da série manual sem código digitado.
                key = await GetKeyByTransactionCode(tc);
            }
            else
            {
                var code = request.TypedCode.Trim();

                if (code.Length > request.MaxLength)
                    throw new ApplicationException($"O número informado passa de {request.MaxLength} caracteres.");

                if (await request.ExistsAsync(code))
                    throw new ApplicationException($"O número {code} já está em uso.");

                return new DocNumberAssignment(series.Key, code);
            }
        }

        const int maxAttempts = 10_000;
        for (var i = 0; i < maxAttempts; i++)
        {
            var code = await GetDocNumber(key);
            if (!await request.ExistsAsync(code))
                return new DocNumberAssignment(key, code);
        }

        throw new ApplicationException($"Não foi possível gerar um número livre para a série \"{series.Name}\".");
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