using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Transport;

/// <summary>Dados do conjunto para inclusão ou alteração. Os reboques vêm na ordem desejada.</summary>
public record TruckCombinationInput(
    string Code, string Description, string TractionTruckCode, string? DefaultDriverCode, bool Inactive,
    IReadOnlyList<string> TrailerCodes);

/// <summary>Conjunto de veículos (só STANDALONE). Grava cabeçalho e reboques de uma vez.</summary>
public class TruckCombinationService(IUnitOfWork db, IConfiguration configuration)
{
    public const string StandaloneOnly = "O conjunto só existe no modo STANDALONE.";
    private const int MaxTrailers = 3;

    public async Task<TruckCombination> SaveAsync(TruckCombinationInput input, bool isNew)
    {
        EnsureStandalone();

        var code = input.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length == 0)
            throw new DefaultException("Informe o código do conjunto.");

        var description = input.Description?.Trim() ?? string.Empty;
        if (description.Length == 0)
            throw new DefaultException("Informe a descrição do conjunto.");

        var traction = await db.Context.Trucks.AsNoTracking().FirstOrDefaultAsync(x => x.Code == input.TractionTruckCode);
        if (traction is null || traction.Kind != TruckKind.Traction)
            throw new DefaultException("Escolha um veículo de tração para o conjunto.");
        // Conjunto sendo inativado aceita veículo inativo: é justamente assim que se aposenta o conjunto.
        var checkActive = !input.Inactive;

        if (checkActive && traction.Inactive)
            throw new DefaultException($"O veículo {traction.Code} está inativo.");

        var trailerCodes = input.TrailerCodes.Select(x => x.Trim().ToUpperInvariant()).ToList();
        if (trailerCodes.Count > MaxTrailers)
            throw new DefaultException("O conjunto aceita no máximo 3 reboques.");

        var repeated = trailerCodes.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
            throw new DefaultException($"O reboque {repeated.Key} aparece mais de uma vez.");

        var trailers = await db.Context.Trucks.AsNoTracking().Where(x => trailerCodes.Contains(x.Code)).ToListAsync();
        foreach (var trailerCode in trailerCodes)
        {
            var trailer = trailers.FirstOrDefault(x => x.Code == trailerCode);
            if (trailer is null || trailer.Kind != TruckKind.Trailer)
                throw new DefaultException($"O veículo {trailerCode} não é reboque.");
            if (checkActive && trailer.Inactive)
                throw new DefaultException($"O veículo {trailerCode} está inativo.");
        }

        var driverCode = string.IsNullOrWhiteSpace(input.DefaultDriverCode) ? null : input.DefaultDriverCode.Trim();
        if (driverCode is not null && !await db.Context.TruckDrivers.AnyAsync(x => x.Code == driverCode))
            throw new DefaultException($"Motorista {driverCode} não encontrado.");

        var combination = await db.Context.TruckCombinations.Include(x => x.Trailers).FirstOrDefaultAsync(x => x.Code == code);

        if (isNew)
        {
            if (combination is not null)
                throw new DefaultException($"Conjunto {code} já cadastrado.");

            combination = new TruckCombination { Code = code, Description = description, TractionTruckCode = traction.Code };
            db.Context.TruckCombinations.Add(combination);
        }
        else if (combination is null)
            throw new DefaultException($"Conjunto {code} não encontrado.");
        else
        {
            db.Context.TruckCombinationTrailers.RemoveRange(combination.Trailers);
            combination.Trailers.Clear();
        }

        combination.Description = description;
        combination.TractionTruckCode = traction.Code;
        combination.DefaultDriverCode = driverCode;
        combination.Inactive = input.Inactive;

        for (var i = 0; i < trailerCodes.Count; i++)
            combination.Trailers.Add(new TruckCombinationTrailer
            {
                CombinationCode = code, Position = i + 1, TrailerTruckCode = trailerCodes[i],
            });

        await db.SaveChangesAsync();

        return combination;
    }

    public async Task DeleteAsync(string code)
    {
        EnsureStandalone();

        var combination = await db.Context.TruckCombinations.Include(x => x.Trailers).FirstOrDefaultAsync(x => x.Code == code)
            ?? throw new DefaultException($"Conjunto {code} não encontrado.");

        db.Context.TruckCombinationTrailers.RemoveRange(combination.Trailers);
        db.Context.TruckCombinations.Remove(combination);
        await db.SaveChangesAsync();
    }

    public void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StandaloneOnly);
    }
}
