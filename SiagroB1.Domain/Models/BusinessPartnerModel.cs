using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Enums;

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

    public string? StateRegistration { get; set; }

    public StateRegistrationIndicator? StateRegistrationIndicator { get; set; }

    public string? NfeEmail { get; set; }

    public string? Phone { get; set; }

    public int? PaymentConditionCode { get; set; }
    
    public ICollection<AddressModel> Addresses { get; set; } = new List<AddressModel>();
}