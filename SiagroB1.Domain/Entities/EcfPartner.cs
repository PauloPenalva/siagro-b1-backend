using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Sócio do quadro societário informado na ECF (registro Y600), por filial.</summary>
[Table("ECF_PARTNERS")]
[Microsoft.EntityFrameworkCore.Index(nameof(BranchCode))]
public class EcfPartner
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string BranchCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string Name { get; set; } = string.Empty;

    /// <summary>CPF ou CNPJ, só dígitos.</summary>
    [Column(TypeName = "VARCHAR(14)")]
    public string TaxId { get; set; } = string.Empty;

    /// <summary>Qualificação do sócio (tabela da ECF), 2 dígitos.</summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string Qualification { get; set; } = string.Empty;

    [Column(TypeName = "DECIMAL(5,2)")]
    public decimal VotingCapitalPercent { get; set; }

    [Column(TypeName = "DECIMAL(5,2)")]
    public decimal TotalCapitalPercent { get; set; }

    /// <summary>Código do país (tabela BACEN), padrão 105 = Brasil.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string CountryCode { get; set; } = "105";
}
