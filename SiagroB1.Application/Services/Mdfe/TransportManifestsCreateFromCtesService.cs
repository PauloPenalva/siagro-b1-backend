using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Dfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Cria o MDF-e em rascunho a partir de CT-e autorizados da filial, herdando tudo o que dá: UFs e município de
/// carregamento (início da prestação do 1º CT-e), descarga agrupada por município, veículos e condutor, seguro da
/// filial + averbações, produto, contratantes (tomadores distintos) e totais. CT-e com UF de início ou tração
/// diferentes não cabem no mesmo MDF-e.
/// </summary>
public class TransportManifestsCreateFromCtesService(IUnitOfWork db, IConfiguration configuration)
{
    public const string DifferentStartUfs = "Os CT-e escolhidos começam em UFs diferentes.";
    public const string DifferentEndUfs = "Os CT-e escolhidos terminam em UFs diferentes.";
    public const string DifferentVehicles = "Os CT-e escolhidos são de veículos diferentes.";

    public async Task<Guid> CreateFromCtesAsync(string branchCode, IReadOnlyList<Guid> cteKeys, string user)
    {
        EnsureStandalone(configuration);
        var keys = cteKeys.Distinct().ToList();
        if (keys.Count == 0) throw new DefaultException("Escolha ao menos um CT-e.");

        var ctes = await db.Context.TransportDocuments.AsNoTracking()
            .Include(d => d.Parties).Include(d => d.Quantities).Include(d => d.Endorsements)
            .Where(d => keys.Contains(d.Key)).ToListAsync();
        // Mantém a ordem escolhida pelo usuário.
        ctes = keys.Select(k => ctes.FirstOrDefault(c => c.Key == k)
                                ?? throw new DefaultException("CT-e não encontrado.")).ToList();

        foreach (var cte in ctes)
            if (cte.BranchCode != branchCode || cte.Status != CteStatus.Authorized)
                throw new DefaultException($"O CT-e {cte.Number} não pode entrar no MDF-e: só CT-e autorizado da filial {branchCode}.");
        if (ctes.Select(c => c.StartState).Distinct().Count() > 1) throw new DefaultException(DifferentStartUfs);
        if (ctes.Select(c => c.EndState).Distinct().Count() > 1) throw new DefaultException(DifferentEndUfs);
        if (ctes.Select(c => c.TractionPlate?.ToUpperInvariant()).Distinct().Count() > 1) throw new DefaultException(DifferentVehicles);

        var first = ctes[0];
        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);
        var transport = await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);
        var cityCodes = ctes.SelectMany(c => new[] { c.StartMunicipalityCode, c.EndMunicipalityCode }).OfType<string>().Distinct().ToList();
        var cities = await db.Context.Municipalities.AsNoTracking().Where(x => cityCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Name);
        string CityName(string? code) => DfeText.Clean(code is not null && cities.TryGetValue(code, out var n) ? n : code, 60);

        var m = new TransportManifest
        {
            BranchCode = branchCode,
            Status = MdfeStatus.Draft,
            Series = settings?.DefaultSeries ?? 1,
            Environment = settings?.Environment,
            UfStart = first.StartState,
            UfEnd = first.EndState,
            TractionPlate = first.TractionPlate?.ToUpperInvariant(),
            DriverCpf = first.DriverCpf,
            DriverName = DfeText.CleanOrNull(first.DriverName, 60),
            CargoType = settings?.DefaultCargoType ?? "02",
            ProductDescription = DfeText.CleanOrNull(first.PredominantProduct, 120),
            CteCount = ctes.Count,
            CargoValue = ctes.Sum(c => c.CargoValue),
            CreatedBy = user,
        };
        MdfeInsuranceSnapshot.Apply(m, transport);
        (m.WeightUnit, m.GrossWeight) = Weight(ctes);

        if (first.StartMunicipalityCode is { } start)
            m.LoadingCities.Add(new TransportManifestLoadingCity { LineNum = 1, CityIbge = start, CityName = CityName(start) });

        var line = 0;
        foreach (var cte in ctes.OrderBy(c => ctes.FindIndex(x => x.EndMunicipalityCode == c.EndMunicipalityCode)))
        {
            var endorsements = string.Join(",", cte.Endorsements.Select(e => e.EndorsementNumber).Where(n => !string.IsNullOrWhiteSpace(n)));
            m.Ctes.Add(new TransportManifestCte
            {
                LineNum = ++line,
                TransportDocumentKey = cte.Key,
                CteAccessKey = cte.AccessKey ?? string.Empty,
                UnloadingCityIbge = cte.EndMunicipalityCode ?? string.Empty,
                UnloadingCityName = CityName(cte.EndMunicipalityCode),
                Endorsements = endorsements.Length == 0 ? null : Cut(endorsements, 2000),
                IsActive = true,
            });
        }

        AddVehicles(m, first);

        foreach (var taker in ctes.Select(Taker).OfType<TransportDocumentParty>()
                     .Select(p => (TaxId: Digits(p.TaxId), Name: DfeText.Clean(p.Name, 60)))
                     .Where(p => p.TaxId.Length > 0).DistinctBy(p => p.TaxId))
            m.Contractors.Add(new TransportManifestContractor { TaxId = taker.TaxId, Name = taker.Name });

        // Checagem e inserção do vínculo na mesma transação; o índice filtrado (IsActive) decide a corrida.
        await db.BeginTransactionAsync();
        try
        {
            await MdfeEligibleCtesQuery.EnsureNotInOtherActiveManifestAsync(db, keys, null);
            db.Context.TransportManifests.Add(m);
            await db.SaveChangesAsync();
            await db.CommitAsync();
            return m.Key;
        }
        catch (DbUpdateException ex)
        {
            await db.RollbackAsync();
            db.Context.ChangeTracker.Clear();
            throw await MdfeEligibleCtesQuery.MapUniqueViolationAsync(db, MdfeEligibleCtesQuery.SqlErrorNumber(ex), keys, m.Key) ?? (Exception)ex;
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    /// <summary>Peso só se todos os CT-e têm quantidade em KG ou TON; misturados, soma em KG. M3 deixa vazio (0).</summary>
    internal static (string Unit, decimal Weight) Weight(IReadOnlyList<TransportDocument> ctes)
    {
        var weights = ctes.Select(c => c.Quantities.FirstOrDefault(q => q.UnitCode is "01" or "02")).ToList();
        if (weights.Any(w => w is null)) return ("01", 0m);
        if (weights.All(w => w!.UnitCode == "02")) return ("02", weights.Sum(w => w!.Quantity));
        return ("01", weights.Sum(w => w!.UnitCode == "02" ? w.Quantity * 1000m : w.Quantity));
    }

    private static TransportDocumentParty? Taker(TransportDocument cte) =>
        cte.Parties.FirstOrDefault(p => (int)p.Role == cte.TakerRole);

    private sealed record VehicleSnapshot(string? Plate, int Kind, string? Renavam, int? TareWeight, int? CapacityKg, int? CapacityM3,
        int? WheelType, int? BodyType, string? State);

    private static void AddVehicles(TransportManifest m, TransportDocument cte)
    {
        var snapshot = string.IsNullOrWhiteSpace(cte.VehicleSnapshotJson)
            ? []
            : JsonSerializer.Deserialize<List<VehicleSnapshot>>(cte.VehicleSnapshotJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var plates = new List<string>();
        if (!string.IsNullOrWhiteSpace(cte.TractionPlate)) plates.Add(cte.TractionPlate);
        plates.AddRange((cte.TrailerPlates ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var line = 0;
        foreach (var plate in plates)
        {
            var traction = line == 0 && plate == cte.TractionPlate;
            var v = snapshot.FirstOrDefault(s => string.Equals(s.Plate, plate, StringComparison.OrdinalIgnoreCase));
            m.Vehicles.Add(ToVehicle(++line, traction, plate, v?.Renavam, v?.TareWeight, v?.CapacityKg, v?.CapacityM3,
                v?.BodyType, v?.WheelType, v?.State));
        }
    }

    internal static TransportManifestVehicle ToVehicle(int line, bool traction, string plate, string? renavam, int? tare, int? capacityKg,
        int? capacityM3, int? bodyType, int? wheelType, string? uf) => new()
    {
        LineNum = line,
        IsTraction = traction,
        Plate = plate.ToUpperInvariant(),
        Renavam = renavam,
        TareKg = tare ?? 0,
        CapacityKg = capacityKg ?? 0,
        CapacityM3 = capacityM3 ?? 0,
        BodyType = (bodyType ?? 0).ToString("00"),
        WheelType = traction && wheelType is { } w ? w.ToString("00") : string.Empty,
        Uf = uf ?? string.Empty,
    };

    internal static void EnsureStandalone(IConfiguration configuration)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchMdfeSettingsService.StandaloneOnly);
    }

    internal static string Digits(string? value) => MdfeReadinessValidator.Digits(value);

    internal static string? Cut(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;
}
