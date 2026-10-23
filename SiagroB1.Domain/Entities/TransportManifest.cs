using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>MDF-e (modelo 58) rodoviário, emitente prestador de serviço. Só STANDALONE.</summary>
[Table("TRANSPORT_MANIFESTS")]
public class TransportManifest
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    // --- Identificação (número, chave e cMDF nascem no Confirmar e nunca mudam) ---
    [Column(TypeName = "VARCHAR(14)")] public required string BranchCode { get; set; }
    public MdfeStatus Status { get; set; } = MdfeStatus.Draft;
    public int Series { get; set; }
    public int? Number { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? RandomCode { get; set; }
    [Column(TypeName = "VARCHAR(44)")] public string? AccessKey { get; set; }
    public DateTime? IssuedAt { get; set; }
    public NfeEnvironment? Environment { get; set; }

    // --- Viagem ---
    [Column(TypeName = "VARCHAR(2)")] public string? UfStart { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? UfEnd { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? TractionPlate { get; set; }
    /// <summary>Condutor principal (os demais ficam em Drivers).</summary>
    [Column(TypeName = "VARCHAR(11)")] public string? DriverCpf { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? DriverName { get; set; }

    // --- Carga (prodPred + tot) ---
    /// <summary>tpCarga "01".."13".</summary>
    [Column(TypeName = "VARCHAR(2)")] public string? CargoType { get; set; }
    [Column(TypeName = "VARCHAR(120)")] public string? ProductDescription { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? Ncm { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? LoadingZip { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? UnloadingZip { get; set; }
    public int CteCount { get; set; }
    [Column(TypeName = "DECIMAL(15,2) DEFAULT 0")] public decimal CargoValue { get; set; }
    /// <summary>cUnid: "01" KG, "02" TON.</summary>
    [Column(TypeName = "VARCHAR(2)")] public string WeightUnit { get; set; } = "01";
    [Column(TypeName = "DECIMAL(15,4) DEFAULT 0")] public decimal GrossWeight { get; set; }

    // --- infANTT ---
    [Column(TypeName = "VARCHAR(12)")] public string? CiotNumber { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? CiotResponsibleTaxId { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? TollSupplierCnpj { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? TollVoucherNumber { get; set; }
    [Column(TypeName = "DECIMAL(15,2)")] public decimal? TollValue { get; set; }

    // --- Seguro (mesmos campos do CT-e) ---
    [Column(TypeName = "VARCHAR(2)")] public string? InsuranceResponsible { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? InsurerTaxId { get; set; }
    [Column(TypeName = "VARCHAR(30)")] public string? InsurerName { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? PolicyNumber { get; set; }

    // --- Observações ---
    [Column(TypeName = "VARCHAR(2000)")] public string? AdditionalInfo { get; set; }

    // --- SEFAZ ---
    [Column(TypeName = "VARCHAR(20)")] public string? ProtocolNumber { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    [Column(TypeName = "VARCHAR(5)")] public string? RejectionCode { get; set; }
    [Column(TypeName = "VARCHAR(500)")] public string? RejectionReason { get; set; }

    // --- Encerramento e cancelamento ---
    /// <summary>Quando o evento de encerramento foi registrado.</summary>
    public DateTime? ClosedAt { get; set; }
    /// <summary>Data do encerramento informada no evento (dtEnc).</summary>
    public DateOnly? ClosedOn { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? ClosingUf { get; set; }
    [Column(TypeName = "VARCHAR(7)")] public string? ClosingCityIbge { get; set; }
    public DateTime? CancelledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [Column(TypeName = "VARCHAR(100)")] public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? UpdatedBy { get; set; }

    public virtual ICollection<TransportManifestCte> Ctes { get; set; } = new List<TransportManifestCte>();
    public virtual ICollection<TransportManifestLoadingCity> LoadingCities { get; set; } = new List<TransportManifestLoadingCity>();
    public virtual ICollection<TransportManifestRouteUf> RouteUfs { get; set; } = new List<TransportManifestRouteUf>();
    public virtual ICollection<TransportManifestVehicle> Vehicles { get; set; } = new List<TransportManifestVehicle>();
    public virtual ICollection<TransportManifestDriver> Drivers { get; set; } = new List<TransportManifestDriver>();
    public virtual ICollection<TransportManifestContractor> Contractors { get; set; } = new List<TransportManifestContractor>();
    public virtual ICollection<TransportManifestPayment> Payments { get; set; } = new List<TransportManifestPayment>();
    public virtual ICollection<TransportManifestSeal> Seals { get; set; } = new List<TransportManifestSeal>();
    public virtual ICollection<TransportManifestXml> Xmls { get; set; } = new List<TransportManifestXml>();
    public virtual ICollection<TransportManifestEvent> Events { get; set; } = new List<TransportManifestEvent>();
}
