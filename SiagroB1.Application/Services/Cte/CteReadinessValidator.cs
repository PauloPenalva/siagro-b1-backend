using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Confere se o CT-e tem tudo o que a SEFAZ exige antes de travar número e chave: filial habilitada, certificado
/// válido, RNTRC, veículos com tara/capacidade/rodado, motorista com CPF válido e natureza resolvida. Junta todos os
/// problemas numa só mensagem, para o usuário corrigir de uma vez.
/// </summary>
public class CteReadinessValidator(IUnitOfWork db)
{
    public async Task EnsureReadyAsync(TransportDocument doc)
    {
        var problems = await CheckAsync(doc);
        if (problems.Count > 0)
            throw new DefaultException(string.Join("\n", problems));
    }

    public async Task<List<string>> CheckAsync(TransportDocument doc)
    {
        var problems = new List<string>();

        var cte = await db.Context.BranchCteSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == doc.BranchCode);
        if (cte is not { Enabled: true })
            problems.Add($"A filial {doc.BranchCode} não está habilitada a emitir CT-e: ative na Configuração do CT-e.");

        var nfe = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == doc.BranchCode);
        if (nfe?.CertificatePfx is not { Length: > 0 })
            problems.Add("A filial não tem certificado digital cadastrado.");
        else if (nfe.CertificateValidUntil is not { } until || until < DateTime.Now)
            problems.Add("O certificado digital da filial está vencido ou sem data de validade.");

        if (string.IsNullOrWhiteSpace(doc.Rntrc))
            problems.Add("Informe o RNTRC da transportadora na Configuração de Transporte da filial.");

        if (doc.UsageCode is null || string.IsNullOrWhiteSpace(doc.Cfop))
            problems.Add("Informe a natureza da operação: sem ela o CT-e não tem CFOP nem tributos.");

        CheckVehicles(doc, problems);

        if (string.IsNullOrWhiteSpace(doc.DriverCpf) || !TaxIdValidator.IsValid(doc.DriverCpf)
            || TaxIdValidator.Normalize(doc.DriverCpf).Length != 11)
            problems.Add("Informe o CPF do motorista (válido).");

        return problems;
    }

    private sealed record VehicleSnapshot(string? Plate, int Kind, int? TareWeight, int? CapacityKg, int? WheelType);

    private static void CheckVehicles(TransportDocument doc, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(doc.TractionPlate))
        {
            problems.Add("Informe o veículo de tração.");
            return;
        }

        var snapshot = new List<VehicleSnapshot>();
        if (!string.IsNullOrWhiteSpace(doc.VehicleSnapshotJson))
            snapshot = JsonSerializer.Deserialize<List<VehicleSnapshot>>(
                doc.VehicleSnapshotJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var plates = new List<(string Plate, bool Traction)> { (doc.TractionPlate, true) };
        plates.AddRange((doc.TrailerPlates ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => (p, false)));

        foreach (var (plate, traction) in plates)
        {
            var vehicle = snapshot.FirstOrDefault(v => string.Equals(v.Plate, plate, StringComparison.OrdinalIgnoreCase));
            var incomplete = vehicle is null
                             || vehicle.TareWeight is not > 0
                             || vehicle.CapacityKg is not > 0
                             || (traction && vehicle.WheelType is null);
            if (incomplete)
                problems.Add($"Veículo {plate} sem tara/capacidade: complete o cadastro.");
        }
    }
}
