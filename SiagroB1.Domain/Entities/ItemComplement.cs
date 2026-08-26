using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Complemento cadastral do produto: atributos que o SiagroB1 precisa e que não existem no mestre
/// de itens. Chaveado só por <see cref="ItemCode"/> — um registro por produto, um campo por
/// atributo; atributos novos (ex.: unidade tributada da NF-e) entram como coluna.
/// <para>
/// Vive sempre no <c>AppDbContext</c>, nos dois modos ERP. Em modo SAPB1 o mestre de itens está no
/// banco do SAP (<c>OITM</c>), então <b>não há FK</b> para <c>ITEMS</c> — e é justamente isso que
/// evita ter de criar campo customizado no SAP. Pela mesma razão não há FK para
/// <c>UNITS_OF_MEASURE</c>, que fica vazia em modo SAPB1 (mesma convenção livre de
/// <see cref="PurchaseContract.UnitOfMeasureCode"/>/<see cref="SalesContract.UnitOfMeasureCode"/>).
/// </para>
/// </summary>
[Table("ITEM_COMPLEMENTS")]
public class ItemComplement
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    /// <summary>
    /// UoM comercial do item (ex.: "SC", "TON"). Nula quando não configurada — as telas caem para a
    /// unidade física (KG), que continua sendo a única fonte de verdade persistida.
    /// </summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CommercialUnitOfMeasureCode { get; set; }

    /// <summary>KG por unidade comercial (ex.: 60 para saca de soja). Nula junto com a UoM.</summary>
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? CommercialFactor { get; set; }
}
