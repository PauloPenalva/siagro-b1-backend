using SiagroB1.Domain.Dtos;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Leitura/gravação do complemento cadastral do armazém
/// (<see cref="Entities.WarehouseComplement"/>). Sempre gravado em <c>AppDbContext</c>,
/// independente do modo ERP (SAPB1/STANDALONE) — não bifurca como <c>IWarehouseService</c>,
/// porque este dado não existe no SAP.
/// </summary>
public interface IWarehouseComplementService
{
    Task<WarehouseComplementDto?> GetAsync(string warehouseCode);

    /// <summary>
    /// Armazéns marcados como próprios. Ausência de registro equivale a "não é próprio", então a
    /// lista é exatamente o conjunto das linhas com <c>IsOwn</c> — falha fechada, mesma convenção
    /// do cadastro.
    /// </summary>
    Task<IEnumerable<WarehouseComplementDto>> GetOwnAsync();

    /// <summary>
    /// Upsert por <c>WarehouseCode</c>. Os dois flags são independentes; <paramref name="notes"/>
    /// vazio equivale a não informado e é gravado como nulo.
    /// </summary>
    Task<WarehouseComplementDto> SetAsync(
        string warehouseCode, bool isParticipant, bool isOwn, string? notes);
}
