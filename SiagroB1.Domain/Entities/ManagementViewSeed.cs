using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// DRE e Balanço padrão semeados pela migration AddManagementViews. As faixas de conta ficam vazias: cada plano de
/// contas é diferente e o contador preenche. Na DRE, deduções e custos/despesas têm sinal Minus (saem positivos) e
/// as fórmulas subtraem.
/// </summary>
public static class ManagementViewSeed
{
    public const string IncomeStatementCode = "DRE";
    public const string BalanceSheetCode = "BP";

    private static ManagementViewLine L(int order, string code, string description, ManagementViewLineKind kind,
        string? parent = null, string? formula = null, bool minus = false, bool bold = false) => new()
    {
        Id = Guid.Parse($"6d000000-0000-4000-8000-{order:000000000000}"),
        Order = order, Code = code, Description = description, Kind = kind, ParentCode = parent, Formula = formula,
        Sign = minus ? ManagementViewLineSign.Minus : ManagementViewLineSign.Plus, Bold = bold,
    };

    public static ManagementView IncomeStatement() => new()
    {
        Code = IncomeStatementCode, Name = "Demonstração do Resultado", Type = ManagementViewType.IncomeStatement,
        UpdatedBy = "migration",
        Lines =
        [
            L(10, "01", "Receita Bruta", ManagementViewLineKind.Accounts, bold: true),
            L(20, "02", "(-) Deduções da Receita", ManagementViewLineKind.Accounts, minus: true),
            L(30, "03", "Receita Líquida", ManagementViewLineKind.Formula, formula: "01 - 02", bold: true),
            L(40, "04", "(-) CMV / CPV", ManagementViewLineKind.Accounts, minus: true),
            L(50, "05", "Lucro Bruto", ManagementViewLineKind.Formula, formula: "03 - 04", bold: true),
            L(60, "06", "(-) Despesas Operacionais", ManagementViewLineKind.Accounts, minus: true),
            L(70, "07", "Resultado Financeiro", ManagementViewLineKind.Accounts),
            L(80, "08", "Resultado antes do IR/CS", ManagementViewLineKind.Formula, formula: "05 - 06 + 07", bold: true),
            L(90, "09", "(-) IR / CS", ManagementViewLineKind.Accounts, minus: true),
            L(100, "10", "Lucro Líquido do Exercício", ManagementViewLineKind.Formula, formula: "08 - 09", bold: true),
        ],
    };

    public static ManagementView BalanceSheet() => new()
    {
        Code = BalanceSheetCode, Name = "Balanço Patrimonial", Type = ManagementViewType.BalanceSheet, UpdatedBy = "migration",
        Lines =
        [
            L(210, "1", "Ativo", ManagementViewLineKind.Total, bold: true),
            L(220, "1.1", "Ativo Circulante", ManagementViewLineKind.Accounts, parent: "1"),
            L(230, "1.2", "Ativo Não Circulante", ManagementViewLineKind.Accounts, parent: "1"),
            L(240, "2", "Passivo e Patrimônio Líquido", ManagementViewLineKind.Total, bold: true),
            L(250, "2.1", "Passivo Circulante", ManagementViewLineKind.Accounts, parent: "2"),
            L(260, "2.2", "Passivo Não Circulante", ManagementViewLineKind.Accounts, parent: "2"),
            L(270, "2.3", "Patrimônio Líquido", ManagementViewLineKind.Accounts, parent: "2"),
        ],
    };
}
