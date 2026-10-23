using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
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
    public async Task<EfdContributionsData> LoadAsync(EfdContributionsRequest request)
    {
        var (branches, declarant) = await CompanyBranches.LoadAsync(db, request.BranchCode);
        var settings = declarant == null ? null : await db.Context.SpedFiscalSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchCode == declarant.Code);

        var establishments = new List<EfdEstablishment>();
        foreach (var branch in branches)
        {
            var (all, _) = await conference.LoadLinesAsync(new FiscalConferenceRequest
            {
                BranchCode = branch.Code, Year = request.Year, Month = request.Month,
            });
            var nonCumulative = branch.PisCofinsRegime == PisCofinsRegime.NonCumulative;
            var lines = all.Where(l => EfdContributionsScope.IsDeclared(l, nonCumulative)).ToList();
            establishments.Add(new EfdEstablishment
            {
                Branch = branch,
                Lines = lines,
                MasterData = await masterData.LoadAsync(branch, lines, null, _ => true),
                // Cancelada/denegada/inutilizada não é receita nem crédito: fica fora da EFD Contribuições.
                VoidDocuments = [],
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
}
