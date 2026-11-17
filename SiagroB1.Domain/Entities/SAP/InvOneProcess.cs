using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Processo fiscal do add-on InvOne (DBInvOne.dbo.Process). Somente leitura; o banco não declara
/// chave e todas as colunas são anuláveis (por isso as propriedades são int?). No SQL Server a
/// entidade é sem chave; só o provider em memória recebe uma chave sombra (ver InvOneDbContext).
/// Toda consulta é AsNoTracking.
/// </summary>
[Table("Process")]
public class InvOneProcess
{
    public int? CompanyId { get; set; }

    /// <summary>13 = A/R Invoice.</summary>
    public int? DocType { get; set; }

    public int? DocEntry { get; set; }

    [Column(TypeName = "NVARCHAR(200)")]
    public string? KeyNfe { get; set; }

    /// <summary>4 = Autorizado, 10 = Cancelado.</summary>
    public int? StatusId { get; set; }

    /// <summary>Nulo = não cancelada.</summary>
    public bool? IsCanceled { get; set; }

    [Column(TypeName = "NVARCHAR(200)")]
    public string? ProtocolNumber { get; set; }
}
