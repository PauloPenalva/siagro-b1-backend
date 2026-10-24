using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Inclusão e edição do CT-e (só STANDALONE). Grava em Rascunho ou Rejeitado (este sem mudar filial/série); o
/// Confirmado precisa ser reaberto antes. Partes, veículos e motorista são copiados do cadastro a cada gravação,
/// e o ICMS/IBS/CBS vem sempre da natureza.
/// </summary>
public class TransportDocumentsSaveService(
    IUnitOfWork db,
    IConfiguration configuration,
    TransportDocumentDefaults defaults,
    TransportDocumentsTaxApplyService taxes)
{
    public const string ConfirmedRequiresReopen = "O CT-e confirmado não pode ser alterado: reabra-o antes de editar.";
    public const string NotEditable = "Só é possível alterar CT-e em rascunho ou rejeitado.";
    public const string RejectedIdentityLocked = "No CT-e rejeitado não é possível mudar a filial, a série ou o número.";
    public const string OnlyDraftDeletes = "Só é possível excluir CT-e em rascunho e sem número.";

    public async Task<TransportDocumentSaveResult> SaveAsync(TransportDocumentInput input, string user)
    {
        EnsureStandalone(configuration);

        TransportDocument doc;
        if (input.Key is { } key)
        {
            doc = await LoadTrackedAsync(key) ?? throw new DefaultException("CT-e não encontrado.");
            EnsureEditable(doc);
            if (doc.Status == CteStatus.Rejected && (doc.BranchCode != input.BranchCode || doc.Series != input.Series))
                throw new DefaultException(RejectedIdentityLocked);
            doc.UpdatedAt = DateTime.Now;
            doc.UpdatedBy = user;
        }
        else
        {
            doc = new TransportDocument { BranchCode = input.BranchCode, CreatedBy = user };
        }

        var keys = ValidateKeys(input.NfeKeys);
        ValidatePartiesShape(input);
        if (input.Components.Sum(c => c.Value) <= 0)
            throw new DefaultException("Informe o valor da prestação.");

        await ValidateFreightOrderAsync(doc, input);

        var parties = new List<TransportDocumentParty>();
        foreach (var p in input.Parties)
            parties.Add(await defaults.SnapshotPartyAsync(p.Role, p.CardCode, p.AddressName));

        doc.BranchCode = input.BranchCode;
        doc.Series = input.Series;
        doc.UsageCode = input.UsageCode;
        // Chave nula = desvincular a ordem: a tela sempre reenvia o vínculo ao gravar.
        doc.FreightOrderKey = input.FreightOrderKey;
        doc.TakerRole = input.TakerRole;
        doc.PredominantProduct = TransportDocumentDefaults.Cut(input.PredominantProduct?.Trim(), 60);
        doc.CargoValue = input.CargoValue;
        doc.Tracked = input.Tracked;
        doc.OwnTransport = input.OwnTransport;
        doc.Observation = input.Observation;
        doc.CostCenterCode = Blank(input.CostCenterCode);
        doc.AccountingItemCode = Blank(input.AccountingItemCode);
        doc.DistributionRuleCode = Blank(input.DistributionRuleCode);
        doc.PaymentConditionCode = input.PaymentConditionCode ?? await TakerPaymentConditionAsync(input);
        doc.InsuranceResponsible = Blank(input.InsuranceResponsible);
        doc.InsurerTaxId = Blank(input.InsurerTaxId);
        doc.InsurerName = Blank(input.InsurerName);
        doc.PolicyNumber = Blank(input.PolicyNumber);

        var transport = await defaults.TransportSettingsAsync(input.BranchCode);
        TransportDocumentDefaults.ApplyInsuranceDefaults(doc, transport);
        doc.Rntrc = transport?.CompanyRntrc;

        ReplaceChildren(doc, parties, keys, input);

        var (suggestedStart, suggestedEnd) = TransportDocumentDefaults.SuggestRoute(parties);
        doc.StartMunicipalityCode = Blank(input.StartMunicipalityCode) ?? suggestedStart;
        doc.EndMunicipalityCode = Blank(input.EndMunicipalityCode) ?? suggestedEnd;
        doc.StartState = await RouteStateAsync(doc.StartMunicipalityCode, parties);
        doc.EndState = await RouteStateAsync(doc.EndMunicipalityCode, parties);

        await SnapshotVehiclesAsync(doc, input);
        await taxes.ApplyAsync(doc);

        var warnings = await KeysInOtherDocumentsAsync(doc.Key, keys);
        // O novo só entra no contexto depois de validado: uma recusa não deixa documento pendurado.
        if (input.Key is null)
            db.Context.TransportDocuments.Add(doc);
        await db.SaveChangesAsync();
        return new TransportDocumentSaveResult(doc.Key, warnings);
    }

    public async Task DeleteAsync(Guid key)
    {
        EnsureStandalone(configuration);
        var doc = await LoadTrackedAsync(key) ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status != CteStatus.Draft || doc.Number is not null)
            throw new DefaultException(OnlyDraftDeletes);
        db.Context.TransportDocuments.Remove(doc);
        await db.SaveChangesAsync();
    }

    internal static void EnsureStandalone(IConfiguration configuration)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);
    }

    internal static void EnsureEditable(TransportDocument doc)
    {
        if (doc.Status == CteStatus.Confirmed) throw new DefaultException(ConfirmedRequiresReopen);
        if (doc.Status is not (CteStatus.Draft or CteStatus.Rejected)) throw new DefaultException(NotEditable);
    }

    internal Task<TransportDocument?> LoadTrackedAsync(Guid key) =>
        db.Context.TransportDocuments
            .Include(x => x.Parties).Include(x => x.NfeKeys).Include(x => x.Quantities).Include(x => x.Components)
            .FirstOrDefaultAsync(x => x.Key == key);

    /// <summary>Vínculo com a ordem: só ordem utilizável e do mesmo tomador; vínculo já existente e inalterado não é revalidado.</summary>
    private async Task ValidateFreightOrderAsync(TransportDocument doc, TransportDocumentInput input)
    {
        if (input.FreightOrderKey is not { } orderKey) return;
        var order = await db.Context.FreightOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Key == orderKey)
                    ?? throw new DefaultException("Ordem de frete não encontrada.");
        if (doc.FreightOrderKey != orderKey)
            FreightOrders.FreightOrdersCreateCteService.EnsureUsable(order);
        var taker = input.Parties.FirstOrDefault(p => (int)p.Role == input.TakerRole);
        if (taker is null || taker.CardCode?.Trim() != order.TakerCardCode.Trim())
            throw new DefaultException(FreightOrders.FreightOrdersCreateCteService.OtherTaker);
    }

    private static List<string> ValidateKeys(IReadOnlyList<string> raw)
    {
        var keys = raw.Select(k => (k ?? string.Empty).Trim()).Where(k => k.Length > 0).ToList();
        if (keys.Count == 0)
            throw new DefaultException("Informe ao menos uma NF-e transportada.");

        var seen = new HashSet<string>();
        foreach (var k in keys)
        {
            if (!CteAccessKey.IsValidNfeKey(k))
                throw new DefaultException($"Chave de NF-e inválida: {k}.");
            if (!seen.Add(k))
                throw new DefaultException($"A chave {k} aparece mais de uma vez.");
        }
        return keys;
    }

    private static void ValidatePartiesShape(TransportDocumentInput input)
    {
        bool Has(CtePartyRole role) => input.Parties.Any(p => p.Role == role && !string.IsNullOrWhiteSpace(p.CardCode));

        if (!Has(CtePartyRole.Sender) || !Has(CtePartyRole.Recipient))
            throw new DefaultException("Informe remetente e destinatário.");
        if (input.TakerRole is < 0 or > 4 || !Has((CtePartyRole)input.TakerRole))
            throw new DefaultException("Informe o tomador do serviço.");
    }

    private void ReplaceChildren(TransportDocument doc, List<TransportDocumentParty> parties, List<string> keys,
        TransportDocumentInput input)
    {
        db.Context.TransportDocumentParties.RemoveRange(doc.Parties);
        db.Context.TransportDocumentNfeKeys.RemoveRange(doc.NfeKeys);
        db.Context.TransportDocumentQuantities.RemoveRange(doc.Quantities);
        db.Context.TransportDocumentComponents.RemoveRange(doc.Components);
        doc.Parties.Clear();
        doc.NfeKeys.Clear();
        doc.Quantities.Clear();
        doc.Components.Clear();

        foreach (var party in parties)
            doc.Parties.Add(party);
        for (var i = 0; i < keys.Count; i++)
            doc.NfeKeys.Add(new TransportDocumentNfeKey { AccessKey = keys[i], Order = i + 1 });
        foreach (var q in input.Quantities)
            doc.Quantities.Add(new TransportDocumentQuantity
            {
                UnitCode = q.UnitCode,
                MeasureType = string.IsNullOrWhiteSpace(q.MeasureType) ? TransportDocumentDefaults.MeasureTypeFor(q.UnitCode) : q.MeasureType.Trim(),
                Quantity = q.Quantity,
            });
        foreach (var c in input.Components)
            doc.Components.Add(new TransportDocumentComponent { Name = c.Name.Trim(), Value = c.Value });

        // Documento já rastreado: filho com Guid preenchido seria tomado como existente (UPDATE) sem o Add explícito.
        if (input.Key is not null)
        {
            db.Context.AddRange(doc.Parties);
            db.Context.AddRange(doc.NfeKeys);
            db.Context.AddRange(doc.Quantities);
            db.Context.AddRange(doc.Components);
        }
    }

    /// <summary>Sugestão da condição de pagamento: a do cadastro do tomador (parte cujo papel é o TakerRole).</summary>
    private async Task<int?> TakerPaymentConditionAsync(TransportDocumentInput input)
    {
        var taker = input.Parties.FirstOrDefault(p => (int)p.Role == input.TakerRole && !string.IsNullOrWhiteSpace(p.CardCode));
        return taker is null ? null : (await defaults.LoadPartnerAsync(taker.CardCode)).PaymentConditionCode;
    }

    private async Task<string?> RouteStateAsync(string? municipalityCode, List<TransportDocumentParty> parties) =>
        await defaults.StateOfAsync(municipalityCode)
        ?? parties.FirstOrDefault(p => p.MunicipalityCode == municipalityCode)?.State;

    private async Task<List<string>> KeysInOtherDocumentsAsync(Guid key, List<string> keys)
    {
        var used = await db.Context.TransportDocumentNfeKeys.AsNoTracking()
            .Where(k => keys.Contains(k.AccessKey) && k.TransportDocumentKey != key
                        && k.TransportDocument!.Status != CteStatus.Cancelled
                        && k.TransportDocument.Status != CteStatus.Voided)
            .Select(k => new { k.AccessKey, k.TransportDocument!.Number, k.TransportDocument.Series })
            .ToListAsync();

        return used
            .Select(u => u.Number is { } n
                ? $"A chave {u.AccessKey} já está no CT-e {n} (série {u.Series})."
                : $"A chave {u.AccessKey} já está em outro CT-e em rascunho.")
            .Distinct()
            .ToList();
    }

    /// <summary>Conjunto → placas da tração e dos reboques; sem conjunto só a tração. Motorista vazio = padrão do conjunto.</summary>
    private async Task SnapshotVehiclesAsync(TransportDocument doc, TransportDocumentInput input)
    {
        var vehicles = new List<Truck>();
        string? defaultDriver = null;
        var combinationCode = Blank(input.TruckCombinationCode);

        if (combinationCode is not null)
        {
            var combination = await db.Context.TruckCombinations.AsNoTracking()
                                  .Include(x => x.Traction)
                                  .Include(x => x.Trailers).ThenInclude(t => t.Trailer)
                                  .FirstOrDefaultAsync(x => x.Code == combinationCode)
                              ?? throw new DefaultException($"Conjunto de veículos {combinationCode} não encontrado.");
            if (combination.Traction is not null) vehicles.Add(combination.Traction);
            vehicles.AddRange(combination.Trailers.OrderBy(t => t.Position).Where(t => t.Trailer is not null).Select(t => t.Trailer!));
            defaultDriver = combination.DefaultDriverCode;
            doc.TruckCombinationCode = combinationCode;
            doc.TractionPlate = combination.TractionTruckCode;
            doc.TrailerPlates = TransportDocumentDefaults.Cut(
                string.Join(",", combination.Trailers.OrderBy(t => t.Position).Select(t => t.TrailerTruckCode)), 100);
            if (doc.TrailerPlates == string.Empty) doc.TrailerPlates = null;
        }
        else
        {
            var plate = Blank(input.TractionPlate)?.ToUpperInvariant();
            doc.TruckCombinationCode = null;
            doc.TractionPlate = plate;
            doc.TrailerPlates = null;
            if (plate is not null && await db.Context.Trucks.AsNoTracking().FirstOrDefaultAsync(t => t.Code == plate) is { } truck)
                vehicles.Add(truck);
        }

        doc.VehicleSnapshotJson = vehicles.Count == 0
            ? null
            : JsonSerializer.Serialize(vehicles.Select(v => new
            {
                Plate = v.Code, v.Kind, v.Renavam, v.TareWeight, v.CapacityKg, v.CapacityM3, v.WheelType, v.BodyType,
                State = v.StateKey, v.OwnerType, v.OwnerCardCode, v.OwnerRntrc, v.OwnerKind,
            }));

        var driverCode = TransportDocumentDefaults.Digits(Blank(input.DriverCpf)) ?? defaultDriver;
        if (driverCode is null)
        {
            doc.DriverCpf = null;
            doc.DriverName = null;
            return;
        }

        var driver = await db.Context.TruckDrivers.AsNoTracking().FirstOrDefaultAsync(d => d.Code == driverCode)
                     ?? throw new DefaultException($"Motorista {driverCode} não encontrado.");
        doc.DriverCpf = driver.Code;
        doc.DriverName = TransportDocumentDefaults.Cut(driver.Name, 60);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
