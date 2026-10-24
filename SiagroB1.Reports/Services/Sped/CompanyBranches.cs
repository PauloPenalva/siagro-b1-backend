using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Reports.Services.Sped;

/// <summary>
/// Filiais da empresa (mesmo CNPJ raiz) da filial pedida, em ordem de CNPJ, e a declarante (matriz /0001).
/// Usado pelas escriturações por empresa (EFD Contribuições, ECD).
/// </summary>
public static class CompanyBranches
{
    private const string HeadOfficeOrder = "0001";

    public static async Task<(List<Branch> Branches, Branch? Declarant)> LoadAsync(IUnitOfWork db, string branchCode)
    {
        var requested = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode);
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
        return (branches.Select(x => x.Branch).ToList(), declarant);
    }

    public static string Digits(string? value) => new((value ?? "").Where(char.IsAsciiDigit).ToArray());
}
