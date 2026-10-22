using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Visão gerencial (CTS do Protheus): estrutura de linhas da DRE ou do Balanço sobre o plano de contas.</summary>
[Table("MANAGEMENT_VIEWS")]
public class ManagementView
{
    [Key]
    [Column(TypeName = "VARCHAR(10)")]
    public string Code { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string Name { get; set; } = string.Empty;

    public ManagementViewType Type { get; set; }

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;

    public List<ManagementViewLine> Lines { get; set; } = new();
}

/// <summary>Linha da visão gerencial.</summary>
[Table("MANAGEMENT_VIEW_LINES")]
public class ManagementViewLine
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(10)")]
    public string ViewCode { get; set; } = string.Empty;

    public int Order { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string Code { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(10)")]
    public string? ParentCode { get; set; }

    public ManagementViewLineKind Kind { get; set; } = ManagementViewLineKind.Accounts;

    [Column(TypeName = "VARCHAR(20)")]
    public string? AccountFrom { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? AccountTo { get; set; }

    public ManagementViewLineSign Sign { get; set; } = ManagementViewLineSign.Plus;

    /// <summary>Só soma e subtração de códigos de linha: "01 - 02 + 05".</summary>
    [Column(TypeName = "VARCHAR(200)")]
    public string? Formula { get; set; }

    public bool Bold { get; set; }

    /// <summary>Não sai no relatório, mas entra nas fórmulas.</summary>
    public bool Hidden { get; set; }
}
