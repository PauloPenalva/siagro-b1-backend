using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class BusinessPartnerModel
{
    [Key]
    public required string CardCode { get; set; }

    public required string CardName { get; set; }
    
    public string? CardFName { get; set; }
    
    public string? CardType { get; set; }
    
    public string? TaxId { get; set; }
    
    public string? QryGroup23 { get;  set; }
    
    public string? Notes { get; set; }
    
    public ICollection<AddressModel> Addresses { get; set; } = new List<AddressModel>();
}