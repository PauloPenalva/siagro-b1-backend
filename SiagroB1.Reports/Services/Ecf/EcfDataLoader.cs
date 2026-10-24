using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;

namespace SiagroB1.Reports.Services.Ecf;

/// <summary>
/// Carga da ECF: a mesma da ECD do ano (<see cref="EcdDataLoader"/> — blocos J/K com os mesmos números) mais os
/// parâmetros, o quadro societário e os ajustes do LALUR/LACS da declarante (matriz /0001).
/// </summary>
public class EcfDataLoader(IUnitOfWork db, EcdDataLoader ledgerLoader)
{
    public async Task<EcfData> LoadAsync(EcfRequest request)
    {
        var ledger = await ledgerLoader.LoadAsync(new EcdRequest { BranchCode = request.BranchCode, Year = request.Year });
        var code = ledger.Declarant?.Code;
        if (code == null) return new EcfData { Ledger = ledger };

        return new EcfData
        {
            Ledger = ledger,
            Settings = await db.Context.EcfSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == code),
            Partners = await db.Context.EcfPartners.AsNoTracking().Where(p => p.BranchCode == code)
                .OrderBy(p => p.Name).ToListAsync(),
            Adjustments = await db.Context.EcfAdjustments.AsNoTracking()
                .Where(a => a.BranchCode == code && a.Year == request.Year)
                .OrderBy(a => a.Period).ThenBy(a => a.Kind).ThenBy(a => a.TableCode).ThenBy(a => a.Description).ToListAsync(),
        };
    }
}
