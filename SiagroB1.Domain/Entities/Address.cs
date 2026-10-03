using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("BUSINESS_PARTNERS_ADDRESSES")]
public class Address 
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column("Address", TypeName = "VARCHAR(15) NOT NULL")]
    public required string AddressName { get; set; }
    
    [Column(TypeName = "VARCHAR(1) NOT NULL")]
    public required string AdresType { get; set; }
    
    public string? Street { get; set; }
    
    public string? Block { get; set; }
    
    public string? ZipCode { get; set; }
    
    public string? City { get; set; }
    
    public string? State { get; set; }
    
    public string? Country { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? StreetNumber { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? Complement { get; set; }

    /// <summary>Município do IBGE. Quando preenchido, <see cref="City"/> e <see cref="State"/> vêm dele.</summary>
    [Column(TypeName = "VARCHAR(7)")]
    [ForeignKey(nameof(Municipality))]
    public string? MunicipalityCode { get; set; }

    public virtual Municipality? Municipality { get; set; }
    
    public virtual BusinessPartner? BusinessPartner { get; set; }
}