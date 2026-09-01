using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Carga: o documento-pivô do fluxo de saída — <c>1:N romaneios → 1 ShipmentLoad ← 1:N
/// documentos de saída</c>. A nota não conhece romaneio: chega nele pela carga.
/// </summary>
/// <remarks>
/// <b>A carga nasce do PLANEJAMENTO, não dos romaneios.</b> A Logística a cria informando placa,
/// motorista, transportadora, produto, armazém e frete — em <see cref="ShipmentLoadStatus.Planned"/>,
/// com <see cref="TotalQuantity"/> zero — e os romaneios de embarque (<c>SalesShipment</c>) são
/// VINCULADOS depois, o que a leva a <see cref="ShipmentLoadStatus.Open"/>. Esvaziá-la a devolve
/// a <c>Planned</c>. O caminho inverso (montar a carga a partir de uma seleção de romaneios) foi
/// removido: existe um único jeito de criar uma carga.
/// <para>
/// Os romaneios vinculados são homogêneos em <see cref="TruckCode"/>, <see cref="ItemCode"/> e
/// <c>BranchCode</c> — essa é a trava. <see cref="WarehouseCode"/> e <see cref="CardCode"/> são
/// informativos e NÃO travam nada.
/// </para>
/// <para>
/// <see cref="TotalQuantity"/> soma <c>GrossWeight</c> dos romaneios, e não <c>NetWeight</c>,
/// por continuidade estrita com o faturamento — é o bruto que vira a quantidade da nota.
/// Trocar mudaria silenciosamente o volume faturado de toda a operação.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOADS")]
[Index(nameof(Code), IsUnique = true)]
public class ShipmentLoad : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }

    public DateTime LoadDate { get; set; } = DateTime.Now.Date;

    public ShipmentLoadStatus Status { get; set; } = ShipmentLoadStatus.Planned;

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; }

    [Column(TypeName = "VARCHAR(11) NULL")]
    public string? TruckDriverCode { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? TruckDriverName { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    /// <summary>
    /// Transportadora do carregamento. Parceiro de negócio, gravado desnormalizado e SEM FK:
    /// em modo SAPB1 as tabelas locais de parceiro ficam vazias e uma FK obrigatória viraria
    /// INNER JOIN, zerando a coleção inteira.
    /// </summary>
    [Column(TypeName = "VARCHAR(10)")]
    public string? CarrierCardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CarrierName { get; set; }

    /// <summary>
    /// Cliente informado pela Logística no planejamento da carga.
    /// </summary>
    /// <remarks>
    /// ⚠️ CAMPO MERAMENTE INFORMATIVO. NÃO participa de nenhum cálculo, NÃO é validado contra
    /// os romaneios vinculados e NÃO alimenta o documento de saída. O cliente real da nota vem
    /// da liberação de entrega escolhida no faturamento — quem fixa o destino é o contrato, não
    /// a carga. Alterar este campo não muda nada no sistema, por decisão do cliente: a Logística
    /// registra o que sabe no momento do planejamento, e a informação correta chega depois,
    /// pelas expedições e pelos documentos de saída. NÃO ligar regra de negócio aqui.
    /// </remarks>
    [Column(TypeName = "VARCHAR(10)")]
    public string? CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Carregamento com excesso de peso ("Excesso S/N" do formulário da Logística).</summary>
    public bool HasExcess { get; set; }

    /// <summary>
    /// Valor do frete negociado para o carregamento. Anulável de propósito, para distinguir
    /// "não informado" de "frete zero". Mesma escala de <c>StorageTransaction.FreightPrice</c>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? FreightPrice { get; set; }

    /// <summary>
    /// Persistido-derivado: soma do <c>GrossWeight</c> dos romaneios VINCULADOS à carga.
    /// Escritor único: <c>ShipmentLoadsRecalculateTotalService</c>, chamado pela vinculação e
    /// pela desvinculação. Nasce ZERO na criação pela Logística, que ainda não tem romaneio —
    /// e é esse zero que mantém a carga em <c>Planned</c>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalQuantity { get; set; }

    /// <summary>
    /// Persistido-derivado: soma das notas VIVAS da carga (pendente e confirmada contam;
    /// cancelada não; devolução CONFIRMADA abate a origem). Escritor único:
    /// <c>ShipmentLoadsRecalculateInvoicedService</c>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal InvoicedQuantity { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    /// <summary>
    /// Motivo informado no cancelamento da carga (obrigatório na ação de cancelar).
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). É a ÚNICA proteção real contra
    /// duas notas parciais simultâneas: ambas passam pelos guards e só aqui a segunda falha.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public virtual ICollection<StorageTransaction> Transactions { get; } = [];

    public virtual ICollection<SalesInvoice> Invoices { get; } = [];

    public virtual ICollection<ShipmentLoadMovement> Movements { get; } = [];

    /// <summary>
    /// Saldo a faturar, derivado dos dois persistidos. Carga cancelada não tem saldo.
    /// </summary>
    [NotMapped]
    public decimal AvailableQuantity =>
        Status != ShipmentLoadStatus.Cancelled
            ? CalculateAvailableQuantity(TotalQuantity, InvoicedQuantity)
            : decimal.Zero;

    /// <summary>
    /// Regra de arredondamento do saldo, compartilhada com quem precisa avaliá-lo antes de
    /// gravar (ex.: <c>ShipmentLoadsBillingGuardService</c>).
    /// </summary>
    public static decimal CalculateAvailableQuantity(decimal totalQuantity, decimal invoicedQuantity) =>
        decimal.Round(totalQuantity - invoicedQuantity, 3, MidpointRounding.ToEven);

    [NotMapped]
    public bool IsFullyInvoiced => AvailableQuantity <= decimal.Zero;
}
