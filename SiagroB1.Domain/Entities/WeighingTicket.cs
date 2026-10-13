using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("WEIGHING_TICKETS")]
[Index("Code", IsUnique = true)]
public class WeighingTicket : DocumentEntity
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public string? Code { get; set; }
    
    public DateTime? Date { get; set; } = DateTime.Now.Date;
    
    public required WeighingTicketType Type { get; set; }

    public WeighingTicketStatus? Status { get; set; } = WeighingTicketStatus.Waiting;

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    [MaxLength(50)]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    [MaxLength(15)]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("Truck")]
    public required string TruckCode { get; set; }
    public virtual Truck? Truck { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    [ForeignKey("TruckDriver")]
    public required string TruckDriverCode { get; set; }
    public virtual TruckDriver? TruckDriver { get; set; }
    
    public int FirstWeighValue { get; set; } = 0;

    [Column(TypeName = "datetime2")]
    public DateTime? FirstWeighDateTime { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? FirstWeighUsername { get; set; }

    /// <summary>Balança que produziu a leitura. Coluna simples, sem FK, como FreightUmCode.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string? FirstWeighScaleCode { get; set; }

    /// <summary>Falso quando o peso foi digitado por quem tem permissão para isso.</summary>
    public bool FirstWeighCaptured { get; set; }

    public int SecondWeighValue { get; set; } = 0;

    [Column(TypeName = "datetime2")]
    public DateTime? SecondWeighDateTime { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? SecondWeighUsername { get; set; }

    [Column(TypeName = "VARCHAR(11)")]
    public string? SecondWeighScaleCode { get; set; }

    public bool SecondWeighCaptured { get; set; }

    [NotMapped]
    public int GrossWeight
    {
        get
        {
            if (FirstWeighValue == 0 ||  SecondWeighValue == 0) return 0;
            return int.Abs(FirstWeighValue - SecondWeighValue);
        }
    }

    public ICollection<QualityInspection> QualityInspections { get; set; } = [];

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? Comments { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    public string? StorageAddressCode { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? ProcessingCostCode { get; set; }

    public WeighingTicketStage? Stage { get; set; } = WeighingTicketStage.ReadyForFirstWeighing;
    
    /// <summary>
    /// Total do Frete/Remoção, derivado por <see cref="RecalculateFreightPrice"/>. Na conclusão, o
    /// romaneio gerado herda <see cref="FreightCalculationMethod"/> e <see cref="FreightUnitPrice"/>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal FreightPrice { get; set; }

    /// <summary>Situação de royalty (ITS). Obrigatória quando o produto controla royalties.</summary>
    public RoyaltySituation? RoyaltySituation { get; set; }

    /// <summary>Código do teste — obrigatório só na situação Testada.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? RoyaltyTestCode { get; set; }

    /// <summary>Parceiro de origem da carga (produtor, ou o armazém participante de onde veio).</summary>
    [Column(TypeName = "VARCHAR(15)")]
    public string? RoyaltyOriginCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? RoyaltyOriginName { get; set; }
    public FreightCalculationMethod FreightCalculationMethod { get; set; } = FreightCalculationMethod.Fixed;

    /// <summary>Valor da viagem (Fixo) ou R$ por saco de 60 kg (Saco).</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal FreightUnitPrice { get; set; }

    /// <summary>
    /// Refaz <see cref="FreightPrice"/> a partir do método, do unitário e do peso bruto. O peso
    /// bruto só existe depois da 2ª pesagem: chamar na edição, na 2ª pesagem e na conclusão.
    /// </summary>
    public void RecalculateFreightPrice()
        => FreightPrice = FreightPriceCalculator.Calculate(FreightCalculationMethod, FreightUnitPrice, GrossWeight);
}