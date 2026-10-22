using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Parte do CT-e (snapshot do cadastro na gravação).</summary>
[Table("TRANSPORT_DOCUMENT_PARTIES")]
public class TransportDocumentParty
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    public CtePartyRole Role { get; set; }
    [Column(TypeName = "VARCHAR(15)")] public string? CardCode { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Name { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? TaxId { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? StateRegistration { get; set; }
    /// <summary>indIEDest/indIEToma: 1 contribuinte, 2 isento, 9 não contribuinte.</summary>
    public int IndIE { get; set; } = 9;
    [Column(TypeName = "VARCHAR(60)")] public string? Street { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Number { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Complement { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? Neighborhood { get; set; }
    [Column(TypeName = "VARCHAR(7)")] public string? MunicipalityCode { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? MunicipalityName { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? State { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? ZipCode { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? Phone { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? Email { get; set; }
}
