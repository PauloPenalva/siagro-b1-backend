using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Importa o XML de uma ou mais NF-e transportadas: cria o rascunho (ou complementa um existente) com remetente =
/// emitente, destinatário = destinatário, as chaves, a soma do vNF, o produto predominante e uma quantidade na
/// unidade padrão da filial (o peso bruto só entra se a unidade for KG — a NF-e não traz M3).
/// </summary>
public class TransportDocumentsImportNfeService(
    IUnitOfWork db,
    IConfiguration configuration,
    TransportDocumentDefaults defaults,
    TransportDocumentsTaxApplyService taxes,
    IBusinessPartnerService partners)
{
    public async Task<Guid> ImportAsync(Guid? key, string branchCode, IReadOnlyList<string> xmls, string user)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);
        if (xmls.Count == 0)
            throw new DefaultException("Selecione ao menos um XML de NF-e.");

        var nfes = xmls.Select(NfeXmlForCteReader.Read).ToList();
        var cardCodes = ResolvePartners(nfes);

        TransportDocument doc;
        if (key is { } existing)
        {
            doc = await db.Context.TransportDocuments
                      .Include(x => x.Parties).Include(x => x.NfeKeys).Include(x => x.Quantities).Include(x => x.Components)
                      .FirstOrDefaultAsync(x => x.Key == existing)
                  ?? throw new DefaultException("CT-e não encontrado.");
            TransportDocumentsSaveService.EnsureEditable(doc);
            doc.UpdatedAt = DateTime.Now;
            doc.UpdatedBy = user;
        }
        else
        {
            doc = new TransportDocument
            {
                BranchCode = branchCode, Series = await defaults.DefaultSeriesAsync(branchCode), CreatedBy = user,
            };
            TransportDocumentDefaults.ApplyInsuranceDefaults(doc, await defaults.TransportSettingsAsync(branchCode));
            doc.Rntrc = (await defaults.TransportSettingsAsync(branchCode))?.CompanyRntrc;
        }

        var added = new List<object>();
        T Track<T>(T child) where T : class { added.Add(child); return child; }

        var first = nfes[0];
        if (doc.Parties.All(p => p.Role != CtePartyRole.Sender))
            doc.Parties.Add(Track(await defaults.SnapshotPartyAsync(CtePartyRole.Sender, cardCodes[Digits(first.Emitter.TaxId)], null, requireIbge: false)));
        if (first.Recipient is not null && doc.Parties.All(p => p.Role != CtePartyRole.Recipient))
            doc.Parties.Add(Track(await defaults.SnapshotPartyAsync(CtePartyRole.Recipient, cardCodes[Digits(first.Recipient.TaxId)], null, requireIbge: false)));

        var unit = await defaults.DefaultUnitAsync(doc.BranchCode);
        var quantity = doc.Quantities.FirstOrDefault(q => q.UnitCode == unit);
        if (quantity is null)
        {
            quantity = Track(new TransportDocumentQuantity { UnitCode = unit, MeasureType = TransportDocumentDefaults.MeasureTypeFor(unit) });
            doc.Quantities.Add(quantity);
        }

        var order = doc.NfeKeys.Count == 0 ? 0 : doc.NfeKeys.Max(k => k.Order);
        foreach (var nfe in nfes)
        {
            if (doc.NfeKeys.Any(k => k.AccessKey == nfe.AccessKey))
                continue; // a mesma NF-e importada de novo não soma outra vez

            doc.NfeKeys.Add(Track(new TransportDocumentNfeKey { AccessKey = nfe.AccessKey, Order = ++order }));
            doc.CargoValue += nfe.TotalValue;
            if (unit == "01")
                quantity.Quantity += nfe.GrossWeight;
        }

        if (string.IsNullOrWhiteSpace(doc.PredominantProduct))
            doc.PredominantProduct = TransportDocumentDefaults.Cut(
                nfes.OrderByDescending(n => n.TotalValue).Select(n => n.PredominantProduct).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)), 60);

        var (start, end) = TransportDocumentDefaults.SuggestRoute(doc.Parties);
        if (string.IsNullOrWhiteSpace(doc.StartMunicipalityCode) && start is not null)
        {
            doc.StartMunicipalityCode = start;
            doc.StartState = await defaults.StateOfAsync(start) ?? doc.Parties.First(p => p.MunicipalityCode == start).State;
        }
        if (string.IsNullOrWhiteSpace(doc.EndMunicipalityCode) && end is not null)
        {
            doc.EndMunicipalityCode = end;
            doc.EndState = await defaults.StateOfAsync(end) ?? doc.Parties.First(p => p.MunicipalityCode == end).State;
        }

        await taxes.ApplyAsync(doc);

        if (key is null)
            db.Context.TransportDocuments.Add(doc);
        else
            // Documento rastreado: filho novo com Guid preenchido seria tomado como existente sem o Add explícito.
            db.Context.AddRange(added);
        await db.SaveChangesAsync();
        return doc.Key;
    }

    /// <summary>CNPJ/CPF → CardCode de todos os emitentes e destinatários; recusa listando os sem cadastro.</summary>
    private Dictionary<string, string> ResolvePartners(List<NfeForCte> nfes)
    {
        var wanted = nfes
            .SelectMany(n => new[] { n.Emitter.TaxId, n.Recipient?.TaxId })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Digits(t!))
            .Distinct()
            .ToList();

        var known = partners.QueryAll()
            .Where(p => p.TaxId != null)
            .Select(p => new { p.CardCode, p.TaxId })
            .AsEnumerable()
            .GroupBy(p => Digits(p.TaxId!))
            .Where(g => wanted.Contains(g.Key))
            .ToDictionary(g => g.Key, g => g.First().CardCode);

        var missing = wanted.Where(t => !known.ContainsKey(t)).ToList();
        if (missing.Count > 0)
            throw new DefaultException($"Cadastre os parceiros antes de importar: {string.Join(", ", missing)}.");

        return known;
    }

    private static string Digits(string value) => TransportDocumentDefaults.Digits(value) ?? string.Empty;
}
