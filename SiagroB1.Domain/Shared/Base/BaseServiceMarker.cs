using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SiagroB1.Domain.Shared.Base;

/// <summary>Marcação da entidade para UPDATE no CRUD genérico.</summary>
public static class BaseServiceMarker
{
    /// <summary>
    /// Marca a entidade como alterada, MENOS as colunas geradas pelo banco.
    ///
    /// `State = Modified` sozinho marca TODAS as propriedades, inclusive as geradas pelo banco.
    /// `BaseEntity.RowId` é `Identity`, e existe em toda entidade do sistema: o UPDATE saía com
    /// ela e o SQL Server recusava com "Cannot update identity column 'RowId'." — ou seja,
    /// nenhum registro podia ser alterado pelo CRUD genérico, em entidade nenhuma.
    ///
    /// O filtro é por `ValueGenerated`, e não pelo nome da coluna, para valer também para
    /// qualquer coluna computada ou com default que venha a ser criada.
    /// </summary>
    public static void MarkForUpdate(EntityEntry entry)
    {
        entry.State = EntityState.Modified;

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.ValueGenerated != ValueGenerated.Never)
            {
                property.IsModified = false;
            }
        }
    }
}
