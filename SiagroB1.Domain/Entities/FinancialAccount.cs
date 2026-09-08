using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conta financeira: caixa ou banco. É por ela que a baixa sai ou entra.
/// Não guarda saldo — a posição de caixa é derivada do ledger de baixas e só ganha tela na Fase 4.
/// </summary>
[Table("FINANCIAL_ACCOUNTS")]
[Index(nameof(BranchCode))]
public class FinancialAccount
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public FinancialAccountType Type { get; set; }

    /// <summary>Código FEBRABAN. Nulo em conta do tipo Caixa.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? BankCode { get; set; }

    /// <summary>
    /// Nome do banco, desnormalizado. Não existe cadastro de bancos e não vale criar um nesta
    /// fase: teria um leitor e nenhum escritor além do próprio usuário.
    /// </summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? BankName { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? BankBranch { get; set; }

    /// <summary>Conta com dígito. TEXTO, nunca numérico — zeros à esquerda são significativos.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? BankAccountNumber { get; set; }

    /// <summary>
    /// A conta é monomoeda. O guard de baixa recusa liquidar documento em moeda diferente
    /// da conta.
    /// </summary>
    [Column(TypeName = "INT DEFAULT 1")]
    public CurrencyType Currency { get; set; } = CurrencyType.Brl;

    /// <summary>
    /// Filial dona da conta; nulo = conta corporativa. FK real porque BRANCHS é tabela LOCAL
    /// nos dois modos de ERP.
    /// </summary>
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public string? BranchCode { get; set; }

    public virtual Branch? Branch { get; set; }

    public bool Inactive { get; set; }
}
