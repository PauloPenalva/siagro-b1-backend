using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_CONTRACTS")]
[Index("Code", IsUnique = true)]
public class SalesContract : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? Complement { get; set; }

    public DateTime? CreationDate { get; set; } = DateTime.Now;

    public ContractType Type { get; set; }
    
    public MarketType? MarketType { get; set; }
    
    public ContractStatus? Status { get; set; } = ContractStatus.Draft;

    /// <summary>
    /// Situação da assinatura do contrato. Independente de <see cref="Status"/> e alterável a
    /// qualquer tempo pela action dedicada — nunca pelo PATCH do cabeçalho, que só aceita
    /// contrato em rascunho. Nulo = não informado.
    /// </summary>
    public SignatureStatus? SignatureStatus { get; set; }

    public int? AgentCode { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? AgentName { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>
    /// Nome fantasia e CNPJ do parceiro, desnormalizados na gravação. Não use
    /// propriedade de navegação: em modo SAPB1 o parceiro vem do SAP e a tabela
    /// local BUSINESS_PARTNERS está vazia — o INNER JOIN zeraria a coleção.
    /// </summary>
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardFName { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CardTaxId { get; set; }

    public DateTime DeliveryStartDate { get; set; }

    public DateTime DeliveryEndDate { get; set; }

    public FreightTerms FreightTerms { get; set; }

    /// <summary>
    /// Custo do frete negociado, por unidade de <see cref="FreightUmCode"/>. Espelha
    /// <c>PurchaseContract.FreightCostStandard</c> e existe para alimentar a futura apuração
    /// de resultado do contrato. Copiado para <c>SalesContractPriceFixation.FreightCost</c>
    /// na fixação automática dos contratos FIX.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FreightCostStandard { get; set; }

    /// <summary>
    /// Unidade de medida do frete. Sem FK nem navegação para <c>UnitOfMeasure</c> — mesmo
    /// motivo de <see cref="UnitOfMeasureCode"/>: em modo SAPB1 a UoM vem do SAP e a tabela
    /// local UNITS_OF_MEASURE está vazia. É assim também no contrato de compra, cuja FK
    /// original foi removida do modelo.
    /// </summary>
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public string? FreightUmCode { get; set; }

    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    // Não declare navegação para a UoM: em modo SAPB1 ela vem do SAP e a tabela
    // local UNITS_OF_MEASURE está vazia. (Havia aqui um
    // [ForeignKey("UnitOfMeasureModel")] apontando para propriedade inexistente.)
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }


    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("HarvestSeason")]
    public required string HarvestSeasonCode { get; set; }
    public virtual HarvestSeason? HarvestSeason { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalVolume { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(LogisticRegionCode))]
    public string? LogisticRegionCode { get; set; }
    public virtual LogisticRegion? LogisticRegion { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Volume { get; set; } = 0;

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal Price { get; set; } = 0;
    
    public CurrencyType? StandardCurrency { get; set; } = CurrencyType.Brl;
    
    public DateTime? StandardCashFlowDate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? PaymentTerms { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }
    
    public ICollection<SalesInvoiceItem>  SalesInvoiceItems { get; set; } = [];

    public ICollection<SalesShipmentRelease> SalesShipmentReleases { get; set; } = [];

    public ICollection<SalesContractAllocation> Allocations { get; set; } = [];

    public ICollection<SalesContractPriceFixation> PriceFixations { get; set; } = [];

    public ICollection<SalesContractAttachment>  Attachments { get; set; } = [];

    public ICollection<SalesContractDeliveryLocation> DeliveryLocations { get; set; } = [];

    /// <summary>
    /// Comentários do contrato: anotações com data, hora e autor, mantidas na tela Detail.
    /// Chama-se <c>CommentEntries</c> e não <c>Comments</c> porque <see cref="Comments"/> já é o
    /// escalar de "Observações" do cabeçalho.
    /// </summary>
    public ICollection<SalesContractComment> CommentEntries { get; set; } = [];

    /// <summary>
    /// Alterações pontuais feitas depois da aprovação (locais de entrega, anexos), o ciclo de
    /// vida das fixações de preço e os comentários.
    /// </summary>
    public ICollection<SalesContractChangeLog> ChangeLogs { get; set; } = [];

    /// <summary>
    /// Volume já fixado (persistido, derivado). Soma <see cref="SalesContractPriceFixation.FixationVolume"/>
    /// das fixações InApproval + Confirmed — uma fixação em aprovação reserva volume para que duas
    /// pessoas não fixem a mesma tonelagem enquanto a diretoria decide. Recalculado exclusivamente por
    /// SalesContractsFixedVolumeService e protegido por <see cref="RowVersion"/>. Não depende de
    /// navegação em runtime — funciona sob $select do OData.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixedVolume { get; set; }

    /// <summary>
    /// Volume alocado (persistido, derivado): Σ Volume assinado das
    /// <see cref="Allocations"/> × fator efetivo do item (item Closed conta
    /// NetQuantity/Quantity — quebra devolve saldo ao contrato). Recalculado
    /// exclusivamente por SalesContractsRecalculateBalanceService (derivado-da-soma,
    /// nunca incremental) e protegido por <see cref="RowVersion"/>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). Protege
    /// <see cref="AllocatedVolume"/> contra alocações/realocações concorrentes e
    /// <see cref="FixedVolume"/> contra fixações concorrentes ao mesmo contrato.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
    
    public void AddAttachment(SalesContractAttachment attachment)
    {
        attachment.SalesContract = this;
        Attachments.Add(attachment);
    }
    
    /// <remarks>
    /// Conta APENAS fixações confirmadas. Uma fixação em aprovação reserva volume
    /// (ver <see cref="FixedVolume"/>) mas não pode entrar no total até ser aprovada
    /// pela diretoria. Espelha <c>PurchaseContract.TotalPrice</c>.
    /// </remarks>
    [NotMapped]
    public decimal TotalPrice =>
        decimal.Round(
            (PriceFixations?
                .Where(x => x.Status == PriceFixationStatus.Confirmed)
                .Sum(x => x.FixationPrice * x.FixationVolume) ?? 0),
            2,
            MidpointRounding.ToEven);

    [NotMapped]
    public decimal AvailableVolumeToPricing =>
        decimal.Round(TotalVolume - FixedVolume, 3, MidpointRounding.ToEven);

    /// <remarks>
    /// LEGADO: a fonte de verdade do consumo agora é <see cref="AllocatedVolume"/>
    /// (ledger SALES_CONTRACTS_ALLOCATIONS). Mantido apenas para conferência/auditoria;
    /// depende de navegação carregada (Include aninhado) para retornar valor correto.
    /// </remarks>
    [NotMapped]
    public decimal TotalVolumeOutgoing => SalesInvoiceItems?
        .Where(x => x.SalesInvoice?.InvoiceStatus is InvoiceStatus.Confirmed or
                    InvoiceStatus.Returned &&
                    x.SalesInvoice.InvoiceType == SalesInvoiceType.Normal)
        .Sum(x => x.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed ? x.NetQuantity : x.Quantity) ?? 0;

    /// <remarks>
    /// LEGADO: ver <see cref="TotalVolumeOutgoing"/>.
    /// </remarks>
    [NotMapped]
    public decimal TotalVolumeIncoming => SalesInvoiceItems?
        .Where(x => x.SalesInvoice?.InvoiceStatus is InvoiceStatus.Confirmed &&
                    x.SalesInvoice.InvoiceType == SalesInvoiceType.Return)
        .Sum(x => x.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed ? x.NetQuantity : x.Quantity) ?? 0;

    /// <summary>
    /// Saldo disponível por nota: TotalVolume − <see cref="AllocatedVolume"/> (persistido,
    /// recalculado a partir do ledger de alocações). Não depende de navegação — funciona
    /// sob $select do OData e sem Includes.
    /// </summary>
    [NotMapped]
    public decimal AvaiableVolume =>
        decimal.Round(TotalVolume - AllocatedVolume, 3, MidpointRounding.ToEven);
    
    [NotMapped]
    public bool HasInvoices => SalesInvoiceItems?
        .Any(i => i.SalesInvoice?.InvoiceStatus != InvoiceStatus.Cancelled) ?? false;

    /// <summary>
    /// Saldo de liberação de entrega. Espelha <c>PurchaseContract</c>: soma o
    /// <see cref="SalesShipmentRelease.ConsumedQuantity"/> de TODAS as liberações
    /// (inclusive canceladas, que contribuem só com o efetivamente romaneado/faturado).
    /// Independente de <see cref="AvaiableVolume"/> (baseado em nota) — coexistem.
    /// </summary>
    [NotMapped]
    public decimal TotalShipmentReleases =>
        decimal.Round(
            (SalesShipmentReleases?
                .Sum(x => x.ConsumedQuantity) ?? 0),
            2, MidpointRounding.ToEven);

    [NotMapped]
    public decimal TotalAvailableToRelease =>
        decimal.Round(TotalVolume - TotalShipmentReleases, 2, MidpointRounding.ToEven);

    [NotMapped]
    public decimal TotalShipmentReleasesWithoutProvisioning =>
        decimal.Round(
            (SalesShipmentReleases?
                .Where(x =>
                    x.Status is ReleaseStatus.Actived or ReleaseStatus.Completed
                             or ReleaseStatus.Paused or ReleaseStatus.Cancelled)
                .Sum(x => x.ConsumedQuantity) ?? 0),
            2, MidpointRounding.ToEven);

    [NotMapped]
    public decimal TotalAvailableToReleaseWithoutProvisioning =>
        decimal.Round(TotalVolume - TotalShipmentReleasesWithoutProvisioning, 2, MidpointRounding.ToEven);

    [NotMapped]
    public bool HasShipmentReleases => SalesShipmentReleases
        .Any(x => x.Status != ReleaseStatus.Cancelled || x.ShippedQuantity > 0);

    /// <summary>
    /// Volume reservado por liberações de entrega ABERTAS (Pending/Actived/Paused) que
    /// ainda não virou nota — o saldo não romaneado de cada uma (<c>AvailableQuantity</c>).
    /// A parte já faturada da liberação já está descontada em <see cref="AvaiableVolume"/>
    /// (via ledger de alocações), então aqui contamos só o não faturado para não duplicar.
    /// </summary>
    /// <remarks>
    /// Piso zero por liberação: o faturamento pode furar a liberação, e uma liberação com saldo
    /// NEGATIVO somada crua diminuiria a reserva — inflando <see cref="PhysicalAvailableToRelease"/>
    /// e deixando liberar volume que não existe. O excedente já está descontado em
    /// <see cref="AvaiableVolume"/>; contá-lo aqui de novo, com sinal invertido, o cancelaria.
    /// </remarks>
    [NotMapped]
    public decimal ReservedByOpenReleases =>
        decimal.Round(
            (SalesShipmentReleases?
                .Where(r => r.Status is ReleaseStatus.Pending or ReleaseStatus.Actived or ReleaseStatus.Paused)
                .Sum(r => Math.Max(decimal.Zero, r.AvailableQuantity)) ?? 0),
            3, MidpointRounding.ToEven);

    /// <summary>
    /// Saldo FÍSICO ainda liberável: o disponível por nota (<see cref="AvaiableVolume"/>,
    /// já líquido do que foi faturado — inclusive por fora de liberação, no fluxo legado)
    /// menos o já reservado por liberações abertas. Impede liberar volume que o contrato
    /// não tem mais para embarcar. Vale: physical = AvaiableVolume − ReservedByOpenReleases.
    /// </summary>
    [NotMapped]
    public decimal PhysicalAvailableToRelease =>
        decimal.Round(AvaiableVolume - ReservedByOpenReleases, 3, MidpointRounding.ToEven);

}