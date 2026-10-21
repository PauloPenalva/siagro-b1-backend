using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>Consulta unificada de centros de custo e regras de distribuição (STANDALONE).</summary>
public class DimensionCodeQueryService(IUnitOfWork db)
{
    public IQueryable<DimensionCodeModel> QueryAll()
    {
        var centers = db.Context.CostCenters
            .Select(x => new DimensionCodeModel
            {
                Code = x.Code,
                Name = x.Name,
                DimensionNumber = x.DimensionNumber,
                Kind = DimensionCodeModel.KindCostCenter,
                Inactive = x.Inactive,
            });

        var rules = db.Context.DistributionRules
            .Select(x => new DimensionCodeModel
            {
                Code = x.Code,
                Name = x.Name,
                DimensionNumber = x.DimensionNumber,
                Kind = DimensionCodeModel.KindRule,
                Inactive = x.Inactive,
            });

        return centers.Concat(rules).AsNoTracking();
    }
}
