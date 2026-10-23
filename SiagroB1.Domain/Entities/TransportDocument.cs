using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>CT-e (modelo 57): conhecimento de transporte rodoviário de carga. Só STANDALONE.</summary>
[Table("TRANSPORT_DOCUMENTS")]
public class TransportDocument
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    // --- Identificação (número, chave e cCT nascem no Confirmar e nunca mudam) ---
    [Column(TypeName = "VARCHAR(14)")] public required string BranchCode { get; set; }
    public int Series { get; set; }
    public int? Number { get; set; }
    [Column(TypeName = "VARCHAR(44)")] public string? AccessKey { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? RandomCode { get; set; }
    public DateTime? IssuedAt { get; set; }
    public CteStatus Status { get; set; } = CteStatus.Draft;
    public NfeEnvironment? Environment { get; set; }

    // --- Natureza ---
    /// <summary>Natureza (USAGES) que define ICMS e IBS/CBS.</summary>
    public int? UsageCode { get; set; }
    [Column(TypeName = "VARCHAR(4)")] public string? Cfop { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? NatureText { get; set; }

    /// <summary>Tomador: 0 remetente, 1 expedidor, 2 recebedor, 3 destinatário, 4 outro.</summary>
    public int TakerRole { get; set; }

    // --- Rota ---
    [Column(TypeName = "VARCHAR(7)")] public string? StartMunicipalityCode { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? StartState { get; set; }
    [Column(TypeName = "VARCHAR(7)")] public string? EndMunicipalityCode { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? EndState { get; set; }

    // --- Carga e prestação ---
    [Column(TypeName = "VARCHAR(60)")] public string? PredominantProduct { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal CargoValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal ServiceTotal { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal AmountToReceive { get; set; }

    // --- ICMS (snapshot da natureza) ---
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsCst { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsBase { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IcmsRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IcmsReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsValue { get; set; }

    // --- IBS/CBS (mesmo formato das linhas das notas) ---
    [Column(TypeName = "VARCHAR(3)")] public string? IbsCbsCst { get; set; }
    [Column(TypeName = "VARCHAR(6)")] public string? IbsCbsClassCode { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsCbsBase { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal CbsRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal CbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal CbsValue { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsStateRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsMunicipalRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsStateValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsMunicipalValue { get; set; }

    // --- Modal rodoviário ---
    [Column(TypeName = "VARCHAR(8)")] public string? Rntrc { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? TruckCombinationCode { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? TractionPlate { get; set; }
    /// <summary>Placas dos reboques separadas por vírgula (PLACA1,PLACA2).</summary>
    [Column(TypeName = "VARCHAR(100)")] public string? TrailerPlates { get; set; }
    /// <summary>Renavam, tara, capacidade, rodado, carroceria e UF de cada veículo, congelados na confirmação.</summary>
    [Column(TypeName = "NVARCHAR(MAX)")] public string? VehicleSnapshotJson { get; set; }
    [Column(TypeName = "VARCHAR(11)")] public string? DriverCpf { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? DriverName { get; set; }

    // --- Seguro ---
    [Column(TypeName = "VARCHAR(2)")] public string? InsuranceResponsible { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? InsurerTaxId { get; set; }
    [Column(TypeName = "VARCHAR(30)")] public string? InsurerName { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? PolicyNumber { get; set; }

    // --- Observações ---
    public bool Tracked { get; set; } = true;
    public bool OwnTransport { get; set; } = true;
    [Column(TypeName = "VARCHAR(2000)")] public string? Observation { get; set; }

    // --- Dimensões contábeis ---
    /// <summary>Centro de custo (CTT). Sem FK: o cadastro é dual-mode.</summary>
    [Column(TypeName = "VARCHAR(20)")] public string? CostCenterCode { get; set; }
    /// <summary>Item contábil (CTD). Sem FK: validado por serviço.</summary>
    [Column(TypeName = "VARCHAR(20)")] public string? AccountingItemCode { get; set; }
    /// <summary>Rateio pré-configurado (CTJ). Exclusivo com centro de custo/item contábil.</summary>
    [Column(TypeName = "VARCHAR(10)")] public string? DistributionRuleCode { get; set; }

    // --- SEFAZ ---
    [Column(TypeName = "VARCHAR(20)")] public string? Protocol { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    [Column(TypeName = "VARCHAR(5)")] public string? StatusCode { get; set; }
    [Column(TypeName = "VARCHAR(500)")] public string? StatusReason { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? CancellationProtocol { get; set; }
    public DateTime? CancelledAt { get; set; }
    [Column(TypeName = "VARCHAR(255)")] public string? CancellationReason { get; set; }

    // --- Averbação (AT&M) ---
    public CteEndorsementStatus EndorsementStatus { get; set; } = CteEndorsementStatus.None;
    [Column(TypeName = "VARCHAR(40)")] public string? EndorsementProtocol { get; set; }
    public DateTime? EndorsedAt { get; set; }
    [Column(TypeName = "VARCHAR(500)")] public string? EndorsementError { get; set; }
    [Column(TypeName = "VARCHAR(40)")] public string? EndorsementCancellationProtocol { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [Column(TypeName = "VARCHAR(100)")] public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? UpdatedBy { get; set; }

    public virtual ICollection<TransportDocumentParty> Parties { get; set; } = new List<TransportDocumentParty>();
    public virtual ICollection<TransportDocumentNfeKey> NfeKeys { get; set; } = new List<TransportDocumentNfeKey>();
    public virtual ICollection<TransportDocumentQuantity> Quantities { get; set; } = new List<TransportDocumentQuantity>();
    public virtual ICollection<TransportDocumentComponent> Components { get; set; } = new List<TransportDocumentComponent>();
    public virtual ICollection<TransportDocumentXml> Xmls { get; set; } = new List<TransportDocumentXml>();
    public virtual ICollection<TransportDocumentCorrection> Corrections { get; set; } = new List<TransportDocumentCorrection>();
    public virtual ICollection<TransportDocumentEndorsement> Endorsements { get; set; } = new List<TransportDocumentEndorsement>();
}
