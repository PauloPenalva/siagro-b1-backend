using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("BUSINESS_PARTNERS")]
public class BusinessPartner
{
    [Key]
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string CardName { get; set; }
    
    [Column("AliasName", TypeName = "VARCHAR(200)")]
    public string? CardFName { get; set; }
    
    /// <summary>
    /// BPType
    /// </summary>
    [Column(TypeName = "VARCHAR(1)")]
    public string? CardType { get; set; }
    
    [Column(name: "U_YKT_CNPJ_CPF",TypeName = "VARCHAR(14)")]
    public string? TaxId { get; set; }

    [Column(TypeName = "VARCHAR(1)")] 
    public string? QryGroup23 { get; set; } = "N";
    
    /// <summary>Série de numeração (Automático/Manual) usada na criação; nula nos cadastros antigos. Imutável, como o código.</summary>
    public Guid? DocNumberKey { get; set; }

    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    
    [Column("Free_Text", TypeName = "VARCHAR(100)")]
    public string? Notes { get; set; }

    // --- Destinatário/transportadora da NF-e STANDALONE (nulos em SAPB1). ---

    [Column(TypeName = "VARCHAR(14)")]
    public string? StateRegistration { get; set; }

    public StateRegistrationIndicator? StateRegistrationIndicator { get; set; }

    [Column(TypeName = "VARCHAR(250)")]
    public string? NfeEmail { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string? Phone { get; set; }

    /// <summary>Condição de pagamento padrão do documento de saída. Sem FK, como o spec pede.</summary>
    public int? PaymentConditionCode { get; set; }

    /// <summary>Parceiro é produtor rural: base da decisão de reter Funrural nas compras.</summary>
    public bool IsRuralProducer { get; set; }

    public RuralProducerType? RuralProducerType { get; set; }

    /// <summary>CPF do produtor pessoa física quando o <see cref="TaxId"/> é um CNPJ. Só dígitos.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string? RuralProducerCpf { get; set; }

    /// <summary>Opção pela contribuição sobre a folha de pagamento (retém apenas o SENAR).</summary>
    public bool RuralPayrollOption { get; set; }
}