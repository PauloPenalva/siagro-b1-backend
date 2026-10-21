using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Interfaces;

/// <summary>Cadastro das 5 dimensões gerenciais (STANDALONE). As linhas são fixas: só há consulta e alteração.</summary>
public interface IAccountingDimensionService
{
    IQueryable<AccountingDimension> QueryAll();
    Task<AccountingDimension?> UpdateAsync(int number, AccountingDimension changes);
}
