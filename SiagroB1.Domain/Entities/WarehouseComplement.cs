using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Complemento cadastral do armazém: atributos que o SiagroB1 precisa e que não existem no mestre
/// de armazéns. Chaveado só por <see cref="WarehouseCode"/> — um registro por armazém, um campo
/// por atributo; atributos novos entram como coluna.
/// <para>
/// Vive sempre no <c>AppDbContext</c>, nos dois modos ERP. Em modo SAPB1 o "armazém" nem é tabela
/// própria: <c>SAP.WarehouseService</c> lê parceiros de negócio (<c>OCRD</c>) filtrados por
/// <c>QryGroup23 = "Y"</c>. Por isso <b>não há FK</b> para <c>WAREHOUSES</c> — é o que evita ter
/// de criar campo customizado no SAP. Mesma convenção de <see cref="ItemComplement"/>.
/// </para>
/// </summary>
[Table("WAREHOUSE_COMPLEMENTS")]
public class WarehouseComplement
{
    /// <summary>Código do armazém (<c>Warehouse.Code</c> em STANDALONE, <c>OCRD.CardCode</c> em SAPB1).</summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string WarehouseCode { get; set; }

    /// <summary>Armazém participante. Ausência de registro equivale a false.</summary>
    [Column(TypeName = "BIT")]
    public bool IsParticipant { get; set; }

    /// <summary>Armazém próprio. Ausência de registro equivale a false.</summary>
    [Column(TypeName = "BIT")]
    public bool IsOwn { get; set; }

    /// <summary>Observações livres sobre o armazém. Vazio é gravado como nulo.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }
}
