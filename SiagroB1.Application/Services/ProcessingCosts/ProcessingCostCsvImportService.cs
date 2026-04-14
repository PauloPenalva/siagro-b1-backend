using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ProcessingCosts;

public class ProcessingCostCsvImportService(IUnitOfWork db) : IProcessingCostCsvImportService
{
    public async Task<ImportResultDto> ImportDryingParametersAsync(
        string processingCostCode,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportResultDto
        {
            ProcessingCostCode = processingCostCode
        };

        if (!IsCsv(file))
        {
            result.Errors.Add("Formato inválido. Utilize somente arquivo .csv.");
            return result;
        }

        var processingCostExists = await db.Context.ProcessingCosts
            .AnyAsync(x => x.Code == processingCostCode, cancellationToken);

        if (!processingCostExists)
        {
            result.Errors.Add($"ProcessingCost '{processingCostCode}' não encontrado.");
            return result;
        }
        
        try
        {
            await using var stream = file.OpenReadStream();
            var rows = await ReadParameterRowsAsync(stream, cancellationToken);

            ValidateParameterRows(rows, result.Errors);

            if (result.Errors.Count > 0)
                return result;
            
            await db.BeginTransactionAsync();
            
            var currentItems = await db.Context.ProcessingCostDryingParameters
                .Where(x => x.ProcessingCostCode == processingCostCode)
                .ToListAsync(cancellationToken);

            db.Context.RemoveRange(currentItems);

            var newItems = rows.Select(x => new ProcessingCostDryingParameter
            {
                ProcessingCostCode = processingCostCode,
                InitialMoisture = x.InitialMoisture,
                FinalMoisture = x.FinalMoisture,
                Rate = x.Rate
            });

            await db.Context.AddRangeAsync(newItems, cancellationToken);
            await db.SaveChangesAsync();
            await db.CommitAsync();

            result.ImportedCount = rows.Count;
            return result;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
            result.Errors.Add($"Erro ao importar parâmetros: {ex.Message}");
            return result;
        }
    }

    public async Task<ImportResultDto> ImportDryingDetailsAsync(
        string processingCostCode,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportResultDto
        {
            ProcessingCostCode = processingCostCode
        };

        if (!IsCsv(file))
        {
            result.Errors.Add("Formato inválido. Utilize somente arquivo .csv.");
            return result;
        }

        var processingCostExists = await db.Context.ProcessingCosts
            .AnyAsync(x => x.Code == processingCostCode, cancellationToken);

        if (!processingCostExists)
        {
            result.Errors.Add($"ProcessingCost '{processingCostCode}' não encontrado.");
            return result;
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var rows = await ReadDetailRowsAsync(stream, cancellationToken);

            ValidateDetailRows(rows, result.Errors);

            if (result.Errors.Count > 0)
                return result;

            await db.BeginTransactionAsync();
            
            var currentItems = await db.Context.ProcessingCostDryingDetails
                .Where(x => x.ProcessingCostCode == processingCostCode)
                .ToListAsync(cancellationToken);

            db.Context.RemoveRange(currentItems);

            var newItems = rows.Select(x => new ProcessingCostDryingDetail
            {
                ProcessingCostCode = processingCostCode,
                InitialMoisture = x.InitialMoisture,
                FinalMoisture = x.FinalMoisture,
                Price = x.Price
            });

            await db.Context.AddRangeAsync(newItems, cancellationToken);
            await db.SaveChangesAsync();
            await db.CommitAsync();

            result.ImportedCount = rows.Count;
            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Erro ao importar detalhes: {ex.Message}");
            return result;
        }
    }

    private static bool IsCsv(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        return string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<ProcessingCostDryingParameterImportRow>> ReadParameterRowsAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var delimiter = await DetectDelimiterAsync(stream, cancellationToken);
        stream.Position = 0;

        using var reader = new StreamReader(stream, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        };

        using var csv = new CsvReader(reader, config);

        var rows = new List<ProcessingCostDryingParameterImportRow>();

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            rows.Add(new ProcessingCostDryingParameterImportRow
            {
                InitialMoisture = ParseDecimal(csv.GetField("InitialMoisture")),
                FinalMoisture = ParseDecimal(csv.GetField("FinalMoisture")),
                Rate = ParseDecimal(csv.GetField("Rate"))
            });
        }

        return rows;
    }

    private static async Task<List<ProcessingCostDryingDetailImportRow>> ReadDetailRowsAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var delimiter = await DetectDelimiterAsync(stream, cancellationToken);
        stream.Position = 0;

        using var reader = new StreamReader(stream, leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        };

        using var csv = new CsvReader(reader, config);

        var rows = new List<ProcessingCostDryingDetailImportRow>();

        await csv.ReadAsync();
        csv.ReadHeader();

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            rows.Add(new ProcessingCostDryingDetailImportRow
            {
                InitialMoisture = ParseDecimal(csv.GetField("InitialMoisture")),
                FinalMoisture = ParseDecimal(csv.GetField("FinalMoisture")),
                Price = ParseDecimal(csv.GetField("Price"))
            });
        }

        return rows;
    }

    private static void ValidateParameterRows(
        List<ProcessingCostDryingParameterImportRow> rows,
        List<string> errors)
    {
        if (rows.Count == 0)
        {
            errors.Add("Nenhuma linha encontrada no CSV.");
            return;
        }

        var duplicated = rows
            .GroupBy(x => new { x.InitialMoisture, x.FinalMoisture })
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicidade encontrada para InitialMoisture={g.Key.InitialMoisture} e FinalMoisture={g.Key.FinalMoisture}.");

        errors.AddRange(duplicated);
    }

    private static void ValidateDetailRows(
        List<ProcessingCostDryingDetailImportRow> rows,
        List<string> errors)
    {
        if (rows.Count == 0)
        {
            errors.Add("Nenhuma linha encontrada no CSV.");
            return;
        }

        var duplicated = rows
            .GroupBy(x => new { x.InitialMoisture, x.FinalMoisture })
            .Where(g => g.Count() > 1)
            .Select(g => $"Duplicidade encontrada para InitialMoisture={g.Key.InitialMoisture} e FinalMoisture={g.Key.FinalMoisture}.");

        errors.AddRange(duplicated);
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0m;

        value = value.Trim();

        if (decimal.TryParse(value, NumberStyles.Any, new CultureInfo("pt-BR"), out var ptBr))
            return ptBr;

        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv))
            return inv;

        throw new InvalidOperationException($"Valor decimal inválido: '{value}'.");
    }

    private static async Task<string> DetectDelimiterAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;

        using var reader = new StreamReader(stream, leaveOpen: true);
        var firstLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;

        if (firstLine.Contains(';'))
            return ";";

        return ",";
    }
}