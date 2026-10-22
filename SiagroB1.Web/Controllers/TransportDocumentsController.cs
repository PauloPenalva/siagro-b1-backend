using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// CT-e (só STANDALONE). Leitura pelo entity set, com $expand dos filhos; inclusão/edição, importação do XML da
/// NF-e e exclusão pelas ações (o PATCH não substitui as listas de filhos).
/// </summary>
public class TransportDocumentsController(
    TransportDocumentsSaveService saveService,
    TransportDocumentsImportNfeService importService,
    IUnitOfWork db,
    IConfiguration configuration) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 2)]
    public IActionResult Get()
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);

        return Ok(db.Context.TransportDocuments.AsNoTracking());
    }

    [EnableQuery(MaxExpansionDepth = 2)]
    public IActionResult Get([FromRoute] Guid key)
    {
        if (!ErpMode.IsStandalone(configuration)) return BadRequest(BranchCteSettingsService.StandaloneOnly);

        return Ok(SingleResult.Create(db.Context.TransportDocuments.AsNoTracking().Where(x => x.Key == key)));
    }

    [HttpPost("odata/TransportDocumentsSave")]
    public async Task<IActionResult> SaveAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios do CT-e ausentes ou inválidos (BranchCode, Series, TakerRole, PartyRoles, PartyCardCodes, NfeKeys, ...).");

        if (!TryParseKey(Text(parameters, "Key"), out var documentKey))
            return BadRequest(InvalidKey);

        try
        {
            var roles = Ints(parameters, "PartyRoles");
            var cards = Texts(parameters, "PartyCardCodes");
            var addresses = Texts(parameters, "PartyAddressNames");
            var units = Texts(parameters, "QuantityUnits");
            var measures = Texts(parameters, "QuantityMeasureTypes");
            var quantities = Decimals(parameters, "QuantityValues");
            var componentNames = Texts(parameters, "ComponentNames");
            var componentValues = Decimals(parameters, "ComponentValues");

            if (roles.Count != cards.Count || units.Count != quantities.Count || componentNames.Count != componentValues.Count)
                return BadRequest("As listas de participantes, quantidades e componentes estão com tamanhos diferentes.");

            var input = new TransportDocumentInput
            {
                Key = documentKey,
                BranchCode = Text(parameters, "BranchCode") ?? string.Empty,
                Series = Int(parameters, "Series") ?? 0,
                UsageCode = Int(parameters, "UsageCode"),
                TakerRole = Int(parameters, "TakerRole") ?? 0,
                Parties = roles.Select((r, i) => new TransportDocumentPartyInput(
                    (CtePartyRole)r, cards[i], i < addresses.Count ? addresses[i] : null)).ToList(),
                StartMunicipalityCode = Text(parameters, "StartMunicipalityCode"),
                EndMunicipalityCode = Text(parameters, "EndMunicipalityCode"),
                NfeKeys = Texts(parameters, "NfeKeys"),
                PredominantProduct = Text(parameters, "PredominantProduct"),
                CargoValue = Decimal(parameters, "CargoValue"),
                Quantities = units.Select((u, i) => new TransportDocumentQuantityInput(
                    u, i < measures.Count ? measures[i] : string.Empty, quantities[i])).ToList(),
                Components = componentNames.Select((n, i) => new TransportDocumentComponentInput(n, componentValues[i])).ToList(),
                TruckCombinationCode = Text(parameters, "TruckCombinationCode"),
                TractionPlate = Text(parameters, "TractionPlate"),
                DriverCpf = Text(parameters, "DriverCpf"),
                InsuranceResponsible = Text(parameters, "InsuranceResponsible"),
                InsurerTaxId = Text(parameters, "InsurerTaxId"),
                InsurerName = Text(parameters, "InsurerName"),
                PolicyNumber = Text(parameters, "PolicyNumber"),
                Tracked = Bool(parameters, "Tracked", true),
                OwnTransport = Bool(parameters, "OwnTransport", true),
                Observation = Text(parameters, "Observation"),
                CostCenterCode = Text(parameters, "CostCenterCode"),
                AccountingItemCode = Text(parameters, "AccountingItemCode"),
                DistributionRuleCode = Text(parameters, "DistributionRuleCode"),
            };

            return Ok(await saveService.SaveAsync(input, User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TransportDocumentsImportNfe")]
    public async Task<IActionResult> ImportNfeAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode") || !parameters.ContainsKey("Xmls"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Xmls (Key opcional).");

        if (!TryParseKey(Text(parameters, "Key"), out var documentKey))
            return BadRequest(InvalidKey);

        try
        {
            var key = await importService.ImportAsync(
                documentKey, Text(parameters, "BranchCode") ?? string.Empty,
                Texts(parameters, "Xmls"), User.Identity?.Name ?? "Unknown");
            return Ok(key);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpPost("odata/TransportDocumentsDelete")]
    public async Task<IActionResult> DeleteAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || GuidOrNull(Text(parameters, "Key")) is not { } key)
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            await saveService.DeleteAsync(key);
            return NoContent();
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static string? Text(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static int? Int(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var value) && value is not null ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;

    private static decimal Decimal(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var value) && value is not null ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0m;

    private static bool Bool(ODataActionParameters p, string name, bool fallback) =>
        p.TryGetValue(name, out var value) && value is not null ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : fallback;

    private static List<object?> Items(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var value) && value is IEnumerable sequence and not string
            ? sequence.Cast<object?>().ToList()
            : [];

    private static List<string> Texts(ODataActionParameters p, string name) =>
        Items(p, name).Select(x => x?.ToString() ?? string.Empty).ToList();

    private static List<int> Ints(ODataActionParameters p, string name) =>
        Items(p, name).Select(x => Convert.ToInt32(x, CultureInfo.InvariantCulture)).ToList();

    private static List<decimal> Decimals(ODataActionParameters p, string name) =>
        Items(p, name).Select(x => x is null ? 0m : Convert.ToDecimal(x, CultureInfo.InvariantCulture)).ToList();

    public const string InvalidKey = "Chave do documento inválida.";

    /// <summary>
    /// Key ausente/vazia = documento novo (true, null); presente e inválida = false — nunca vira inclusão, senão uma
    /// edição com chave malformada criaria um CT-e duplicado.
    /// </summary>
    public static bool TryParseKey(string? raw, out Guid? key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!Guid.TryParse(Uri.UnescapeDataString(raw.Trim()), out var parsed)) return false;
        key = parsed;
        return true;
    }

    private static Guid? GuidOrNull(string? value) =>
        Guid.TryParse(Uri.UnescapeDataString(value ?? string.Empty), out var g) ? g : null;
}
