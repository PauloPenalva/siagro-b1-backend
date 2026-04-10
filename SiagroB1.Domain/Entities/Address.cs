using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;

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
    
    public virtual BusinessPartner? BusinessPartner { get; set; }
}