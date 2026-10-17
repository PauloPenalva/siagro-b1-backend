using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Credor e dia de vencimento do título de cada tributo retido (spec 2026-10-17 §2). Uma linha por tributo; a migration
/// <c>SeedWithholdingTaxCreditors</c> semeia os três órgãos (Receita Federal, Fazenda estadual, Prefeitura) para o usuário completar.
/// </summary>
[Table("WITHHOLDING_TAX_SETTINGS")]
public class WithholdingTaxSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int TaxType { get; set; }

    /// <summary>
    /// A chave é INT (e não o enum) porque o OData V4 do UI5 não resolve chave enum no PATCH/DELETE. Este atalho é para o
    /// código: busque por <c>(int)WithholdingTaxType.Iss</c> e leia o tributo com <c>GetTax()</c>.
    /// </summary>
    public WithholdingTaxType GetTax() => (WithholdingTaxType)TaxType;

    /// <summary>Parceiro (fornecedor) credor do tributo.</summary>
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Dia do mês do vencimento (1–31; em mês curto vale o último dia).</summary>
    public int DueDay { get; set; }
}
