using System.Linq.Expressions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// GAC-1196: quando um cadastro do SAP (OCRD/OITM) conta como INATIVO na data informada.
///
/// Regra do próprio SAP B1:
/// - "Inativo" (<c>frozenFor = 'Y'</c>): bloqueado; com datas, só dentro de frozenFrom..frozenTo.
/// - "Ativo" (<c>validFor = 'Y'</c>) com datas: bloqueado FORA de validFrom..validTo.
/// - Sem nenhum dos dois marcados, o cadastro é ativo.
///
/// É uma Expression para o EF traduzir: a lista e o value help filtram por ela no SQL.
/// </summary>
public static class SapRecordActivity
{
    public static Expression<Func<T, bool>> IsInactive<T>(DateTime today) where T : class, ISapActivityFlags
    {
        var day = today.Date;

        return x =>
            (x.FrozenFor == "Y"
             && (x.FrozenFrom == null || x.FrozenFrom <= day)
             && (x.FrozenTo == null || x.FrozenTo >= day))
            || (x.ValidFor == "Y"
                && ((x.ValidFrom != null && x.ValidFrom > day)
                    || (x.ValidTo != null && x.ValidTo < day)));
    }
}
