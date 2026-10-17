
using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IItemService
{
    Task<IEnumerable<ItemModel>> GetAllAsync();
    Task<ItemModel?> GetByIdAsync(string code);
    Task<ItemModel> CreateAsync(ItemModel entity);
    Task<ItemModel?> UpdateAsync(string code, ItemModel entity);
    Task<bool> DeleteAsync(string code);
    IQueryable<ItemModel> QueryAll();

    /// <summary>Qualquer produto ativo (Enabled = "SIM"), de qualquer grupo: a linha de "outra compra" (serviço, uso e consumo, ativo). Em SAPB1 segue o filtro de <see cref="QueryAll"/>.</summary>
    IQueryable<ItemModel> QueryAllActive();

    /// <summary>Um produto ativo de qualquer grupo pelo código. Em SAPB1 segue o filtro de <see cref="GetByIdAsync"/>.</summary>
    Task<ItemModel?> GetActiveByIdAsync(string code);
    Task<bool> DeleteAsyncWithTransaction(string code, Func<ItemModel, Task>? preDeleteAction = null);
}