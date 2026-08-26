using SiagroB1.Domain.Dtos;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Leitura/gravação do complemento cadastral do item (<see cref="Entities.ItemComplement"/>).
/// Sempre gravado em <c>AppDbContext</c>, independente do modo ERP (SAPB1/STANDALONE) — não
/// bifurca como <c>IItemService</c>, porque este dado não existe no SAP.
/// </summary>
public interface IItemComplementService
{
    Task<ItemComplementDto?> GetAsync(string itemCode);

    /// <summary>
    /// Upsert por <c>ItemCode</c>. Os dois campos são opcionais: passar ambos nulos limpa o
    /// complemento, que é uma operação válida.
    /// </summary>
    Task<ItemComplementDto> SetAsync(
        string itemCode, string? commercialUnitOfMeasureCode, decimal? commercialFactor);
}
