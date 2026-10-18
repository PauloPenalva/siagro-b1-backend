using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

public class BusinessPartnerModel
{
    /// <summary>Em STANDALONE pode chegar vazio quando a série escolhida é automática: o serviço gera o código.</summary>
    [Key]
    public string CardCode { get; set; } = string.Empty;

    /// <summary>Série de numeração escolhida (Automático/Manual). Gravada na criação e imutável depois.</summary>
    public Guid? DocNumberKey { get; set; }

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

    public bool IsRuralProducer { get; set; }

    public RuralProducerType? RuralProducerType { get; set; }

    public string? RuralProducerCpf { get; set; }

    public bool RuralPayrollOption { get; set; }
    
    public ICollection<AddressModel> Addresses { get; set; } = new List<AddressModel>();
}