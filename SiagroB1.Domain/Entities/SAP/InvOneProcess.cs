using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Processo fiscal do add-on InvOne (DBInvOne.dbo.Process). Somente leitura; o banco não declara
/// chave. O mapeamento do EF usa uma chave composta apenas para o provider em memória aceitar a
/// entidade; por isso toda consulta é AsNoTracking (sem resolução de identidade, linhas
/// duplicadas continuam distintas).
/// </summary>
[Table("Process")]
public class InvOneProcess
{
    public int CompanyId { get; set; }

    /// <summary>13 = A/R Invoice.</summary>
    public int DocType { get; set; }

    public int DocEntry { get; set; }

    [Column(TypeName = "NVARCHAR(200)")]
    public string? KeyNfe { get; set; }

    /// <summary>4 = Autorizado, 10 = Cancelado.</summary>
    public int StatusId { get; set; }

    /// <summary>Nulo = não cancelada.</summary>
    public bool? IsCanceled { get; set; }

    [Column(TypeName = "NVARCHAR(200)")]
    public string? ProtocolNumber { get; set; }
}
