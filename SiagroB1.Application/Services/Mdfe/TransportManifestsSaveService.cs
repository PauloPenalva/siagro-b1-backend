using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Dfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Gravação do MDF-e em rascunho (só Draft) e exclusão do rascunho. Documentos (CPF/CNPJ/CIOT/CEP) ficam só com
/// dígitos; o CteCount sai das linhas de CT-e. Os vínculos de CT-e não mudam aqui.
/// </summary>
public class TransportManifestsSaveService(IUnitOfWork db, IConfiguration configuration, MdfeReadinessValidator validator)
{
    public const string OnlyDraft = "Só é possível alterar MDF-e em rascunho.";
    public const string OnlyDraftDeletes = "Só é possível excluir MDF-e em rascunho e sem número.";
    public const string NotFound = "MDF-e não encontrado.";

    public async Task<IReadOnlyList<string>> SaveAsync(Guid key, TransportManifestSaveModel model, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);

        // A gravação não mexe nos vínculos de CT-e (só a inclusão os cria), então não há corrida a guardar aqui.
        var m = await LoadTrackedAsync(key) ?? throw new DefaultException(NotFound);
        if (m.Status != MdfeStatus.Draft) throw new DefaultException(OnlyDraft);

        await ApplyAsync(m, model);
        MdfeInsuranceSnapshot.RefreshIfIncomplete(m,
            await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == m.BranchCode));
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = user;
        await db.SaveChangesAsync();
        return (await validator.ValidateAsync(m)).Warnings;
    }

    public async Task DeleteAsync(Guid key)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);
        var m = await LoadTrackedAsync(key) ?? throw new DefaultException(NotFound);
        if (m.Status != MdfeStatus.Draft || m.Number is not null) throw new DefaultException(OnlyDraftDeletes);
        db.Context.TransportManifests.Remove(m);
        await db.SaveChangesAsync();
    }

    private Task<TransportManifest?> LoadTrackedAsync(Guid key) => db.Context.TransportManifests
        .Include(x => x.Ctes).Include(x => x.LoadingCities).Include(x => x.RouteUfs).Include(x => x.Vehicles)
        .Include(x => x.Drivers).Include(x => x.Contractors).Include(x => x.Seals)
        .Include(x => x.Payments).ThenInclude(p => p.Components)
        .Include(x => x.Payments).ThenInclude(p => p.Installments)
        .AsSplitQuery()
        .FirstOrDefaultAsync(x => x.Key == key);

    private async Task ApplyAsync(TransportManifest m, TransportManifestSaveModel model)
    {
        var digits = TransportManifestsCreateFromCtesService.Digits;

        m.CteCount = m.Ctes.Count;
        m.LoadingZip = Blank(digits(model.LoadingZip));
        m.UnloadingZip = Blank(digits(model.UnloadingZip));
        m.WeightUnit = string.IsNullOrWhiteSpace(model.WeightUnit) ? "01" : model.WeightUnit.Trim();
        m.GrossWeight = model.GrossWeight;
        m.CiotNumber = Blank(digits(model.CiotNumber));
        m.CiotResponsibleTaxId = Blank(digits(model.CiotResponsibleTaxId));
        m.TollSupplierCnpj = Blank(digits(model.TollSupplierCnpj));
        m.TollVoucherNumber = DfeText.CleanOrNull(model.TollVoucherNumber, 20);
        m.TollValue = model.TollValue;
        m.AdditionalInfo = DfeText.CleanOrNull(model.AdditionalInfo, 2000);

        Replace(m.RouteUfs, model.RouteUfs.Select(u => u.Trim().ToUpperInvariant()).Where(u => u.Length == 2)
            .Select((u, i) => new TransportManifestRouteUf { LineNum = i + 1, Uf = u }));
        Replace(m.Seals, model.Seals.Select(s => DfeText.CleanOrNull(s, 20)).OfType<string>()
            .Select(s => new TransportManifestSeal { Number = s }));
        Replace(m.Drivers, model.AdditionalDrivers.Where(d => digits(d.Cpf).Length > 0)
            .Select((d, i) => new TransportManifestDriver { LineNum = i + 1, Cpf = digits(d.Cpf), Name = DfeText.Clean(d.Name ?? string.Empty, 60) }));
        Replace(m.Contractors, model.Contractors.Select(c => (TaxId: digits(c.TaxId), c.Name)).Where(c => c.TaxId.Length > 0)
            .DistinctBy(c => c.TaxId).Select(c => new TransportManifestContractor { TaxId = c.TaxId, Name = DfeText.Clean(c.Name, 60) }));
        Replace(m.Payments, model.Payments.Select(ToPayment));

        await ReplaceTrailersAsync(m, model.TrailerPlates);
    }

    private static TransportManifestPayment ToPayment(TransportManifestPaymentModel p)
    {
        var payment = new TransportManifestPayment
        {
            ContractorTaxId = MdfeReadinessValidator.Digits(p.ContractorTaxId),
            ContractorName = DfeText.Clean(p.ContractorName, 60),
            Total = p.Total,
            IndPag = p.IndPag,
            Pix = DfeText.CleanOrNull(p.Pix, 60),
            BankCode = Blank(MdfeReadinessValidator.Digits(p.BankCode)),
            Agency = Blank(MdfeReadinessValidator.Digits(p.Agency)),
        };
        foreach (var c in p.Components)
            payment.Components.Add(new TransportManifestPaymentComponent { Type = c.Type, Value = c.Value, Description = DfeText.CleanOrNull(c.Description, 60) });
        foreach (var i in p.Installments)
            payment.Installments.Add(new TransportManifestPaymentInstallment { Number = i.Number, DueDate = i.DueDate, Value = i.Value });
        return payment;
    }

    /// <summary>Reboques vêm do cadastro de veículos; a tração (herdada do CT-e) fica.</summary>
    private async Task ReplaceTrailersAsync(TransportManifest m, IReadOnlyList<string> plates)
    {
        var wanted = plates.Select(p => new string(p.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant())
            .Where(p => p.Length > 0).Distinct().ToList();
        var current = m.Vehicles.Where(v => !v.IsTraction).Select(v => v.Plate).ToList();
        if (current.SequenceEqual(wanted)) return;

        var trucks = await db.Context.Trucks.AsNoTracking().Where(t => wanted.Contains(t.Code)).ToDictionaryAsync(t => t.Code);
        foreach (var old in m.Vehicles.Where(v => !v.IsTraction).ToList()) m.Vehicles.Remove(old);
        var line = m.Vehicles.Count;
        foreach (var plate in wanted)
        {
            var t = trucks.GetValueOrDefault(plate) ?? throw new DefaultException($"Veículo {plate} não encontrado.");
            var vehicle = TransportManifestsCreateFromCtesService.ToVehicle(++line, false, plate, t.Renavam, t.TareWeight, t.CapacityKg,
                t.CapacityM3, t.BodyType is { } b ? (int)b : null, null, t.StateKey);
            m.Vehicles.Add(vehicle);
            db.Context.Add(vehicle);
        }
    }

    /// <summary>Troca a lista; os novos entram como Added (Guid já preenchido faria o EF tratar como Update).</summary>
    private void Replace<T>(ICollection<T> target, IEnumerable<T> items) where T : class
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
            db.Context.Add(item);
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
