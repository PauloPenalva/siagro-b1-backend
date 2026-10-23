using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Services.EfdContributions;

/// <summary>
/// Lê do banco a EFD Contribuições do mês: a escrituração é por empresa (CNPJ raiz), então
/// junta todas as filiais com a mesma raiz, cada uma com as linhas da Conferência Fiscal e o
/// cadastro do bloco 0. Toda NF-e tem C170 nesse arquivo, por isso todas as linhas contam.
/// </summary>
public class EfdContributionsDataLoader(IUnitOfWork db, FiscalConferenceService conference, SpedMasterDataLoader masterData)
{
    private const string HeadOfficeOrder = "0001";

    public async Task<EfdContributionsData> LoadAsync(EfdContributionsRequest request)
    {
        var requested = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == request.BranchCode);
        var root = Digits(requested?.TaxId);
        if (root.Length < 8) throw new BusinessException("Filial sem CNPJ.");
        root = root[..8];

        // TaxId é formatado no banco; a raiz é comparada só pelos dígitos, em memória.
        var branches = (await db.Context.Branchs.AsNoTracking().Where(b => b.TaxId != null).ToListAsync())
            .Select(b => (Branch: b, Cnpj: Digits(b.TaxId)))
            .Where(x => x.Cnpj.Length >= 8 && x.Cnpj[..8] == root)
            .OrderBy(x => x.Cnpj, StringComparer.Ordinal)
            .ToList();

        var declarant = branches.FirstOrDefault(x => x.Cnpj.Length >= 12 && x.Cnpj[8..12] == HeadOfficeOrder).Branch;
        var settings = declarant == null ? null : await db.Context.SpedFiscalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchCode == declarant.Code);

        var establishments = new List<EfdEstablishment>();
        foreach (var (branch, _) in branches)
        {
            var (lines, _) = await conference.LoadLinesAsync(new FiscalConferenceRequest
            {
                BranchCode = branch.Code, Year = request.Year, Month = request.Month,
            });
            establishments.Add(new EfdEstablishment
            {
                Branch = branch,
                Lines = lines,
                MasterData = await masterData.LoadAsync(branch, lines, null, _ => true),
                VoidDocuments = await SpedVoidDocumentLoader.LoadAsync(db, branch.Code ?? "", request.Year, request.Month),
            });
        }

        var usageCodes = establishments.SelectMany(e => e.Lines).Select(l => l.UsageCode)
            .OfType<int>().Distinct().ToArray();
        var usages = await db.Context.Usages.AsNoTracking().Where(u => usageCodes.Contains(u.Code))
            .ToDictionaryAsync(u => u.Code);

        return new EfdContributionsData
        {
            Declarant = declarant, Settings = settings, Establishments = establishments, Usages = usages,
        };
    }

    private static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
