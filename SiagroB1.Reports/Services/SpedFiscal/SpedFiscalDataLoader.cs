using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services.SpedFiscal;

/// <summary>
/// Lê do banco tudo o que o SPED Fiscal do mês precisa: linhas escrituráveis (as mesmas da
/// Conferência Fiscal), NF-e próprias canceladas/denegadas/inutilizadas, participantes, produtos,
/// unidades, fatores de conversão e, opcionalmente, o inventário.
/// </summary>
public class SpedFiscalDataLoader(IUnitOfWork db, FiscalConferenceService conference, FiscalInventoryService inventory)
{
    /// <summary>Unidade física em que os fatores do <see cref="ItemComplement"/> são expressos.</summary>
    private const string PhysicalUnit = "KG";

    public async Task<SpedFiscalData> LoadAsync(SpedFiscalRequest request)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == request.BranchCode)
                     ?? throw new InvalidOperationException($"Filial {request.BranchCode} não encontrada.");
        var settings = await db.Context.SpedFiscalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchCode == request.BranchCode);

        var (lines, _) = await conference.LoadLinesAsync(new FiscalConferenceRequest
        {
            BranchCode = request.BranchCode, Year = request.Year, Month = request.Month,
        });

        FiscalInventoryData? inventoryData = null;
        if (request.IncludeInventory)
            inventoryData = await inventory.BuildAsync(new FiscalInventoryRequest
            {
                BranchCode = request.BranchCode,
                Date = request.InventoryDate ?? new DateTime(request.Year - 1, 12, 31),
            });

        var (items, conversions, unconvertible) = await LoadItemsAsync(branch, lines, inventoryData);
        var participants = await LoadParticipantsAsync(lines);

        return new SpedFiscalData
        {
            Branch = branch,
            Settings = settings,
            Lines = lines,
            VoidDocuments = await LoadVoidDocumentsAsync(request),
            Participants = participants,
            MissingParticipantCodes = lines.Select(l => l.CardCode).Where(c => c != "").Distinct()
                .Except(participants.Select(p => p.Code)).Order(StringComparer.Ordinal).ToList(),
            UnconvertibleUnits = unconvertible,
            Items = items,
            Units = await LoadUnitsAsync(items, lines, conversions),
            Conversions = conversions,
            Inventory = inventoryData,
        };
    }

    /// <summary>
    /// NF-e própria do mês com situação cancelada (02), denegada (04) ou inutilizada (05). Não filtra
    /// pela situação do documento: a NF-e cancelada costuma ter o documento cancelado também.
    /// </summary>
    private async Task<List<SpedVoidDocument>> LoadVoidDocumentsAsync(SpedFiscalRequest request)
    {
        var from = new DateTime(request.Year, request.Month, 1);
        var toExclusive = from.AddMonths(1);
        NfeStatus[] statuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

        var sales = await db.Context.SalesInvoices.AsNoTracking()
            .Where(s => s.BranchCode == request.BranchCode && s.TaxDocumentKind == TaxDocumentKind.Nfe)
            .Where(s => statuses.Contains(s.NfeStatus))
            .Where(s => s.InvoiceDate >= from && s.InvoiceDate < toExclusive)
            .Select(s => new
            {
                s.NfeStatus, s.TaxDocumentSeries, s.TaxDocumentNumber, s.InvoiceNumber, s.ChaveNFe,
                IsReturn = s.InvoiceType == SalesInvoiceType.Return,
                Cfop = s.Items.OrderBy(i => i.NfeItemNumber).Select(i => i.Cfop).FirstOrDefault(),
            })
            .ToListAsync();

        var purchases = await db.Context.PurchaseInvoices.AsNoTracking()
            .Where(p => p.BranchCode == request.BranchCode && p.TaxDocumentKind == TaxDocumentKind.Nfe)
            .Where(p => p.IssuerType == DocumentIssuerType.Own)
            .Where(p => statuses.Contains(p.NfeStatus))
            .Where(p => (p.PostingDate ?? p.IssueDate) >= from && (p.PostingDate ?? p.IssueDate) < toExclusive)
            .Select(p => new
            {
                p.NfeStatus, p.TaxDocumentSeries, p.TaxDocumentNumber, p.InvoiceNumber, p.ChaveNFe,
                IsReturn = p.InvoiceType == PurchaseInvoiceType.Return,
                Cfop = p.Items.OrderBy(i => i.NfeItemNumber).Select(i => i.Cfop).FirstOrDefault(),
            })
            .ToListAsync();

        // IND_OPER pelo sentido da operação, como no C100 regular: CFOP da 1ª linha; sem CFOP, o tipo
        // (devolução de venda é entrada, devolução de compra própria é saída)
        return sales.Select(s => Void(Direction(s.Cfop, s.IsReturn ? "0" : "1"), s.NfeStatus, s.TaxDocumentSeries, s.TaxDocumentNumber ?? s.InvoiceNumber, s.ChaveNFe))
            .Concat(purchases.Select(p => Void(Direction(p.Cfop, p.IsReturn ? "1" : "0"), p.NfeStatus, p.TaxDocumentSeries, p.TaxDocumentNumber ?? p.InvoiceNumber, p.ChaveNFe)))
            .OrderBy(v => v.Direction, StringComparer.Ordinal)
            .ThenBy(v => v.Series, StringComparer.Ordinal)
            .ThenBy(v => v.Number, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>"0" entrada / "1" saída pelo 1º dígito do CFOP; sem CFOP válido, o padrão do tipo.</summary>
    private static string Direction(string? cfop, string fallback) => cfop?.Trim() switch
    {
        [var first, ..] when first is '1' or '2' or '3' => "0",
        [var first, ..] when first is '5' or '6' or '7' => "1",
        _ => fallback,
    };

    private static SpedVoidDocument Void(string direction, NfeStatus status, string? series, string? number, string? key) => new()
    {
        Direction = direction,
        Situation = status switch
        {
            NfeStatus.Cancelled => "02",
            NfeStatus.Denied => "04",
            _ => "05",
        },
        Model = "55",
        Series = series ?? "",
        Number = number ?? "",
        AccessKey = key,
    };

    /// <summary>Só os parceiros das linhas; o C100 de documento cancelado não leva participante.</summary>
    private async Task<List<SpedParticipant>> LoadParticipantsAsync(List<FiscalDocumentLineDto> lines)
    {
        var codes = lines.Select(l => l.CardCode).Where(c => c != "").Distinct().ToArray();
        var partners = await db.Context.BusinessPartners.AsNoTracking()
            .Include(p => p.Addresses)
            .Where(p => codes.Contains(p.CardCode))
            .ToListAsync();

        return partners
            .Select(p =>
            {
                var address = p.Addresses
                    .OrderBy(a => a.AddressName, StringComparer.Ordinal)
                    .ThenBy(a => a.AdresType, StringComparer.Ordinal)
                    .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.MunicipalityCode));
                var (cnpj, cpf) = TaxIds(p);
                return new SpedParticipant
                {
                    Code = p.CardCode,
                    Name = p.CardName,
                    Cnpj = cnpj,
                    Cpf = cpf,
                    StateRegistration = Digits(p.StateRegistration),
                    MunicipalityCode = address?.MunicipalityCode ?? "",
                    Street = address?.Street ?? "",
                    Number = address?.StreetNumber ?? "",
                    Complement = address?.Complement ?? "",
                    District = address?.Block ?? "",
                };
            })
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Produtor rural pessoa física com CPF próprio sai pelo CPF, mesmo com CNPJ no cadastro.</summary>
    private static (string Cnpj, string Cpf) TaxIds(BusinessPartner partner)
    {
        if (partner.RuralProducerType == RuralProducerType.Individual && !string.IsNullOrWhiteSpace(partner.RuralProducerCpf))
            return ("", Digits(partner.RuralProducerCpf));

        var taxId = Digits(partner.TaxId);
        return taxId.Length switch
        {
            14 => (taxId, ""),
            11 => ("", taxId),
            _ => ("", ""),
        };
    }

    private static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());

    /// <summary>
    /// Produtos das linhas (e do inventário). O cadastro não tem unidade de estoque, então a
    /// unidade do 0200 é a mais usada nas linhas do produto (empate: ordem alfabética); sem linha,
    /// a do inventário. Linha em outra unidade gera 0220 quando o fator sai do complemento.
    /// </summary>
    public async Task<(List<SpedItem> Items, List<SpedUnitConversion> Conversions, List<SpedUnconvertibleUnit> Unconvertible)> LoadItemsAsync(
        Branch branch, List<FiscalDocumentLineDto> lines, FiscalInventoryData? inventoryData)
    {
        // Unidades que de fato serão escritas: C170 (documento de terceiro) e H010 (inventário). A
        // NF-e própria não tem C170, então a unidade dela não exige 0220 nem bloqueia o arquivo.
        var lineUnits = lines
            .Where(l => l.ItemCode != "")
            .GroupBy(l => l.ItemCode)
            .ToDictionary(g => g.Key, g => g.Where(l => l.IssuerType == "1").Select(l => l.UnitOfMeasureCode).Where(u => u != "").ToList());
        var ownUnits = lines
            .Where(l => l.ItemCode != "" && l.IssuerType != "1")
            .GroupBy(l => l.ItemCode)
            .ToDictionary(g => g.Key, g => g.Select(l => l.UnitOfMeasureCode).Where(u => u != "").ToList());
        foreach (var row in inventoryData?.Rows.Where(r => r.Quantity > 0) ?? [])
        {
            if (!lineUnits.TryGetValue(row.ItemCode, out var list)) lineUnits[row.ItemCode] = list = [];
            if (!string.IsNullOrWhiteSpace(row.UnitOfMeasureCode)) list.Add(row.UnitOfMeasureCode);
        }

        var codes = lineUnits.Keys.ToArray();
        var catalog = await db.Context.Items.AsNoTracking().Where(i => codes.Contains(i.ItemCode)).ToDictionaryAsync(i => i.ItemCode);
        var complements = await db.Context.ItemComplements.AsNoTracking().Where(c => codes.Contains(c.ItemCode)).ToDictionaryAsync(c => c.ItemCode);
        var nameFromLines = lines.GroupBy(l => l.ItemCode).ToDictionary(g => g.Key, g => g.First().ItemName);

        var items = new List<SpedItem>();
        var conversions = new List<SpedUnitConversion>();
        var unconvertible = new List<SpedUnconvertibleUnit>();
        foreach (var code in codes.Order(StringComparer.Ordinal))
        {
            var units = lineUnits[code];
            // Sem unidade escrita (só NF-e própria), a do 0200 sai das linhas próprias
            var unit = (units.Count > 0 ? units : ownUnits.GetValueOrDefault(code) ?? [])
                .GroupBy(u => u)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key)
                .FirstOrDefault() ?? "";
            catalog.TryGetValue(code, out var item);
            complements.TryGetValue(code, out var complement);

            items.Add(new SpedItem
            {
                Code = code,
                Description = item?.ItemName ?? nameFromLines.GetValueOrDefault(code) ?? code,
                UnitCode = unit,
                ItemType = code == branch.FreightServiceItemCode ? "09" : "00",
                Ncm = Digits(item?.Ncm),
                Cest = Digits(item?.Cest),
                GoodsOrigin = item?.GoodsOrigin,
            });

            foreach (var other in units.Distinct().Where(u => u != unit).Order(StringComparer.Ordinal))
            {
                var factor = Factor(other, unit, complement);
                if (factor != null)
                    conversions.Add(new SpedUnitConversion { ItemCode = code, UnitCode = other, Factor = factor.Value });
                else
                    unconvertible.Add(new SpedUnconvertibleUnit(code, other));
            }
        }

        return (items, conversions, unconvertible);
    }

    /// <summary>Quantas unidades do produto cabem em 1 <paramref name="from"/>, pelos fatores (em KG) do complemento.</summary>
    private static decimal? Factor(string from, string to, ItemComplement? complement)
    {
        var fromKg = KgPerUnit(from, complement);
        var toKg = KgPerUnit(to, complement);
        return fromKg is > 0 && toKg is > 0 ? Math.Round(fromKg.Value / toKg.Value, 6) : null;
    }

    private static decimal? KgPerUnit(string unit, ItemComplement? complement)
    {
        if (string.Equals(unit, PhysicalUnit, StringComparison.OrdinalIgnoreCase)) return 1m;
        if (complement == null) return null;
        if (string.Equals(unit, complement.CommercialUnitOfMeasureCode, StringComparison.OrdinalIgnoreCase)) return complement.CommercialFactor;
        if (string.Equals(unit, complement.TaxableUnitOfMeasureCode, StringComparison.OrdinalIgnoreCase)) return complement.TaxableFactor;
        return null;
    }

    /// <summary>Unidades dos produtos, das linhas e das conversões, com a descrição do cadastro.</summary>
    private async Task<List<SpedUnit>> LoadUnitsAsync(
        List<SpedItem> items, List<FiscalDocumentLineDto> lines, List<SpedUnitConversion> conversions)
    {
        var codes = items.Select(i => i.UnitCode)
            .Concat(lines.Select(l => l.UnitOfMeasureCode))
            .Concat(conversions.Select(c => c.UnitCode))
            .Where(u => u != "")
            .Distinct()
            .ToArray();
        var descriptions = await db.Context.UnitsOfMeasure.AsNoTracking()
            .Where(u => codes.Contains(u.Code))
            .ToDictionaryAsync(u => u.Code, u => u.Description);

        return codes
            .Order(StringComparer.Ordinal)
            .Select(c => new SpedUnit { Code = c, Description = descriptions.GetValueOrDefault(c) ?? c })
            .ToList();
    }
}
