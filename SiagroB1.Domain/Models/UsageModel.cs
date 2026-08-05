using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>
/// Natureza de operação exposta pela API. Dual-mode: em STANDALONE vem de USAGES (só os
/// dois CFOPs de saída preenchidos); em SAPB1 vem de OUSG, que traz os seis CFOPs mas
/// não conhece os efeitos de negócio.
/// </summary>
public class UsageModel
{
    [Key]
    public int Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }
    
    public string? CfopIncomingInState { get; set; }
    
    public string? CfopIncomingOutState { get; set; }
    
    public string? CfopIncomingImport { get; set; }
    
    public string? CfopOutgoingInState { get; set; }
    
    public string? CfopOutgoingOutState { get; set; }
    
    public string? CfopOutgoingExport { get; set; }

    public ContractBalanceEffect ContractBalanceEffect { get; set; }

    public ContractValueEffect ContractValueEffect { get; set; }

    public bool RequiresContract { get; set; }

    public bool RequiresQuantity { get; set; } = true;

    public bool RequiresWeight { get; set; }

    /// <summary>Natureza aplicada ao faturamento de romaneio — ver <see cref="Entities.UsageEffect.IsDefault"/>.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Existe linha de efeito cadastrada para esta natureza?
    ///
    /// Distingue "ninguém configurou" de "configurado como sem efeito", que dão exatamente os
    /// mesmos valores nos campos acima. Sem essa diferença, uma natureza recém-chegada do
    /// <c>OUSG</c> passaria por "não altera nada" e o documento nasceria sem efeito em
    /// silêncio — que é o default silencioso que a spec proíbe.
    /// </summary>
    public bool HasConfiguredEffects { get; set; }

    public bool Inactive { get; set; }
}