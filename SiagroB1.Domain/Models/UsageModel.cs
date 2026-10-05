using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>
/// Natureza de operação exposta pela API. Dual-mode: em STANDALONE vem de USAGES (CFOPs de
/// saída ou de entrada, conforme o tipo, mais a tributação da NF-e); em SAPB1 vem de OUSG, que
/// traz os seis CFOPs mas não conhece os efeitos de negócio — e ali os campos de tributação
/// voltam nulos, porque a tributação é do SAP.
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

    /// <summary>Nulo em SAPB1: o OUSG não tem tipo. Enum não anulável quebraria a serialização (0 não é membro).</summary>
    public UsageDirection? Direction { get; set; }

    public string? InvoiceOperationText { get; set; }
    public string? DefaultAdditionalInfo { get; set; }
    public bool MovesFiscalInventory { get; set; }
    public bool CreatesFinancialDocument { get; set; }

    public string? IcmsInStateCst { get; set; }
    public string? IcmsInStateCsosn { get; set; }
    public decimal? IcmsInStateRate { get; set; }
    public decimal? IcmsInStateBaseReduction { get; set; }
    public decimal? IcmsInStateDeferral { get; set; }
    public string? IcmsInStateBenefitCode { get; set; }

    public string? IcmsOutStateCst { get; set; }
    public string? IcmsOutStateCsosn { get; set; }
    public decimal? IcmsOutStateBaseReduction { get; set; }
    public decimal? IcmsOutStateDeferral { get; set; }
    public string? IcmsOutStateBenefitCode { get; set; }

    public string? PisCst { get; set; }
    public decimal? PisRate { get; set; }
    public string? CofinsCst { get; set; }
    public decimal? CofinsRate { get; set; }
    public bool ExcludeIcmsFromPisCofinsBase { get; set; }

    public string? IbsCbsCst { get; set; }
    public string? IbsCbsClassCode { get; set; }
    public decimal? IbsRateReduction { get; set; }
    public decimal? CbsRateReduction { get; set; }

    /// <summary>Natureza de entrada da NF-e de devolução (só natureza de Saída, só STANDALONE).</summary>
    public int? ReturnUsageCode { get; set; }

    /// <summary>Nome da natureza de devolução — só leitura (projeção); o PATCH o ignora.</summary>
    public string? ReturnUsageName { get; set; }

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