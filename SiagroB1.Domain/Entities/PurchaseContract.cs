using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS")]
[Index("Code", IsUnique = true)]
public class PurchaseContract : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? Complement { get; set; }

    public DateTime? CreationDate { get; set; } = DateTime.Now;
    
    public ContractType Type { get; set; }
    
    public MarketType? MarketType { get; set; }
    
    public ContractStatus? Status { get; set; } = ContractStatus.Draft;
    
    public int? AgentCode { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")] 
    public string? AgentName { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string? CardName { get; set; }

    public DateTime DeliveryStartDate { get; set; }

    public DateTime DeliveryEndDate { get; set; }

    public FreightTerms FreightTerms { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FreightCostStandard { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public string? FreightUmCode { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]  
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("HarvestSeason")]
    public required string HarvestSeasonCode { get; set; }
    public virtual HarvestSeason? HarvestSeason { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalVolume { get; set; }
    
    [Column(TypeName = "DECIMAL(18,6) DEFAULT 0")]
    public decimal StandardPrice { get; set; }

    public CurrencyType? StandardCurrency { get; set; } = CurrencyType.Brl;
    
    public DateTime? StandardCashFlowDate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? PaymentTerms { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string DeliveryLocationCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string? DeliveryLocationName { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(LogisticRegion))]
    public string? LogisticRegionCode { get; set; }
    public virtual LogisticRegion? LogisticRegion { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }
    
    public ICollection<PurchaseContractPriceFixation> PriceFixations { get; set; } = [];
    
    public ICollection<PurchaseContractTax> Taxes { get; set; } = [];
    
    public ICollection<PurchaseContractQualityParameter>  QualityParameters { get; set; } = [];
    
    public ICollection<PurchaseContractBroker> Brokers { get; set; } = [];
    
    public ICollection<ShipmentRelease> ShipmentReleases { get; set; } = [];
    
    public ICollection<PurchaseContractAllocation> Allocations { get; set; } = [];
    
    public ICollection<PurchaseContractAttachment> Attachments { get; set; } = [];

    /// <summary>
    /// Comentários do contrato: anotações com data, hora e autor, mantidas na tela Detail.
    /// Chama-se <c>CommentEntries</c> e não <c>Comments</c> porque <see cref="Comments"/> já é o
    /// escalar de "Observações" do cabeçalho.
    /// </summary>
    public ICollection<PurchaseContractComment> CommentEntries { get; set; } = [];

    /// <summary>
    /// Log de alterações do contrato — hoje, o ciclo de vida das fixações de preço e os
    /// comentários.
    /// </summary>
    public ICollection<PurchaseContractChangeLog> ChangeLogs { get; set; } = [];
    
    public TechnologyType? TechnologyType { get; set; }

    public FunruralType? FunruralType { get; set; } = Enums.FunruralType.Bruto;

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>
    /// Volume já fixado (persistido, derivado). Soma <see cref="PurchaseContractPriceFixation.FixationVolume"/>
    /// das fixações InApproval + Confirmed — uma fixação em aprovação reserva volume para que duas
    /// pessoas não fixem a mesma tonelagem enquanto a diretoria decide.
    /// Recalculado exclusivamente por PurchaseContractsFixedVolumeService e protegido por
    /// <see cref="RowVersion"/>. Não depende de navegação em runtime — funciona sob $select do OData.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixedVolume { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). Protege
    /// <see cref="AllocatedVolume"/> contra alocações concorrentes ao mesmo contrato.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public void AddAttachment(PurchaseContractAttachment attachment)
    {
        attachment.PurchaseContract = this;
        Attachments.Add(attachment);
    }

    [NotMapped]
    public decimal TotalStandard =>
        decimal.Round(TotalVolume * StandardPrice, 2, MidpointRounding.ToEven);
    
    [NotMapped]
    public decimal AvailableVolumeToPricing => TotalVolume - FixedVolume;
    
    /// <remarks>
    /// Conta APENAS fixações confirmadas. Uma fixação em aprovação reserva volume
    /// (ver <see cref="FixedVolume"/>) mas não pode contaminar a base tributária —
    /// PurchaseContractTax.TotalTax deriva deste valor.
    /// </remarks>
    [NotMapped]
    public decimal TotalPrice =>
        decimal.Round(
            (PriceFixations?
                .Where(x => x.Status == PriceFixationStatus.Confirmed)
                .Sum(x => x.FixationPrice * x.FixationVolume) ?? 0),
            2 ,
            MidpointRounding.ToEven) ;
    
    [NotMapped]
    public decimal TotalTax => 
        decimal.Round((Taxes?.Sum(x => x.TotalTax) ?? 0), 2, MidpointRounding.ToEven);
    
    /// <remarks>
    /// Soma <see cref="ShipmentRelease.ConsumedQuantity"/> de TODAS as liberações,
    /// inclusive canceladas: uma liberação cancelada contribui apenas com o que foi
    /// efetivamente romaneado (zero quando nunca houve movimentação).
    /// </remarks>
    [NotMapped]
    public decimal TotalShipmentReleases =>
        decimal.Round(
            (ShipmentReleases?
                .Sum(x => x.ConsumedQuantity) ?? 0),
        2, MidpointRounding.ToEven);
    
    [NotMapped]
    public decimal TotalAvailableToRelease => 
        decimal.Round(TotalVolume - TotalShipmentReleases, 2, MidpointRounding.ToEven);
    
    [NotMapped]
    public decimal TotalShipmentReleasesWithoutProvisioning =>
        decimal.Round(
            (ShipmentReleases?
                .Where(x =>
                    x.Status is ReleaseStatus.Actived or ReleaseStatus.Completed
                             or ReleaseStatus.Paused or ReleaseStatus.Cancelled)
                .Sum(x => x.ConsumedQuantity) ?? 0),
            2, MidpointRounding.ToEven);
    
    [NotMapped]
    public decimal TotalAvailableToReleaseWithoutProvisioning => 
        decimal.Round(TotalVolume - TotalShipmentReleasesWithoutProvisioning, 2, MidpointRounding.ToEven);
    
    /// <remarks>
    /// Uma liberação cancelada COM movimentação continua bloqueando (houve movimento físico).
    /// </remarks>
    [NotMapped]
    public bool HasShipmentReleases => ShipmentReleases
        .Any(x => x.Status != ReleaseStatus.Cancelled || x.ShippedQuantity > 0);
    
    /// <summary>
    /// Saldo alocável do contrato, derivado de <see cref="AllocatedVolume"/>
    /// (persistido, recalculado nos serviços de alocação). Não depende de
    /// nenhuma navegação em runtime — funciona sob $select do OData.
    /// </summary>
    [NotMapped]
    public decimal AvaiableVolume =>
        decimal.Round(TotalVolume - AllocatedVolume, 2, MidpointRounding.ToEven);
}