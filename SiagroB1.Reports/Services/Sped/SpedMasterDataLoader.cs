using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services.Sped;

/// <summary>Cadastro do bloco 0 comum aos arquivos SPED (0150, 0190, 0200, 0220).</summary>
public class SpedMasterData
{
    public List<SpedParticipant> Participants { get; set; } = [];
    public List<SpedItem> Items { get; set; } = [];
    public List<SpedUnit> Units { get; set; } = [];
    public List<SpedUnitConversion> Conversions { get; set; } = [];

    /// <summary>CardCodes das linhas sem cadastro de parceiro (ordem ordinal).</summary>
    public List<string> MissingParticipantCodes { get; set; } = [];

    /// <summary>Unidade escrita diferente da do 0200 e sem fator no complemento.</summary>
    public List<SpedUnconvertibleUnit> UnconvertibleUnits { get; set; } = [];
}

/// <summary>
/// Monta participantes, produtos, unidades e conversões a partir das linhas escrituráveis e do
/// inventário. Cada arquivo SPED decide, via <c>writesItemLine</c>, quais linhas são detalhadas
/// (e por isso contam para unidade/0220): SPED Fiscal só as de terceiro; Contribuições todas.
/// Linha do inventário sempre conta como escrita.
/// </summary>
public class SpedMasterDataLoader(IUnitOfWork db)
{
    /// <summary>Unidade física em que os fatores do <see cref="ItemComplement"/> são expressos.</summary>
    private const string PhysicalUnit = "KG";

    /// <param name="branch">Filial do arquivo: o item de serviço de frete dela sai com tipo 09.</param>
    public async Task<SpedMasterData> LoadAsync(Branch branch, IReadOnlyCollection<FiscalDocumentLineDto> lines,
        IReadOnlyCollection<FiscalInventoryRowDto>? inventory, Func<FiscalDocumentLineDto, bool> writesItemLine)
    {
        var (items, conversions, unconvertible) = await LoadItemsAsync(branch, lines, inventory, writesItemLine);
        var participants = await LoadParticipantsAsync(lines);
        return new SpedMasterData
        {
            Participants = participants,
            MissingParticipantCodes = lines.Select(l => l.CardCode).Where(c => c != "").Distinct()
                .Except(participants.Select(p => p.Code)).Order(StringComparer.Ordinal).ToList(),
            UnconvertibleUnits = unconvertible,
            Items = items,
            Units = await LoadUnitsAsync(items, lines, conversions),
            Conversions = conversions,
        };
    }

    /// <summary>Só os parceiros das linhas; o C100 de documento cancelado não leva participante.</summary>
    private async Task<List<SpedParticipant>> LoadParticipantsAsync(IReadOnlyCollection<FiscalDocumentLineDto> lines)
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
    private async Task<(List<SpedItem> Items, List<SpedUnitConversion> Conversions, List<SpedUnconvertibleUnit> Unconvertible)> LoadItemsAsync(
        Branch branch, IReadOnlyCollection<FiscalDocumentLineDto> lines, IReadOnlyCollection<FiscalInventoryRowDto>? inventory,
        Func<FiscalDocumentLineDto, bool> writesItemLine)
    {
        // Unidades que de fato serão escritas: as linhas que o arquivo detalha (writesItemLine) e o
        // inventário. Linha não escrita não exige 0220 nem bloqueia o arquivo.
        var lineUnits = lines
            .Where(l => l.ItemCode != "")
            .GroupBy(l => l.ItemCode)
            .ToDictionary(g => g.Key, g => g.Where(writesItemLine).Select(l => l.UnitOfMeasureCode).Where(u => u != "").ToList());
        var ownUnits = lines
            .Where(l => l.ItemCode != "" && !writesItemLine(l))
            .GroupBy(l => l.ItemCode)
            .ToDictionary(g => g.Key, g => g.Select(l => l.UnitOfMeasureCode).Where(u => u != "").ToList());
        foreach (var row in inventory?.Where(r => r.Quantity > 0) ?? [])
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
            // Sem unidade escrita (só linhas não escritas), a do 0200 sai dessas linhas
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
        List<SpedItem> items, IReadOnlyCollection<FiscalDocumentLineDto> lines, List<SpedUnitConversion> conversions)
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
