using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Baixa feita no ITS (sistema da Bayer) registrada no SiagroB1. Uma baixa cobre um ou mais
/// romaneios de recebimento, cada um com o peso baixado (pode ser parcial). Não se edita:
/// errou, cancela e refaz — cancelar devolve o saldo aos romaneios.
/// </summary>
[Table("ROYALTY_WRITE_OFFS")]
public class RoyaltyWriteOff : BaseEntity
{
    [Column(TypeName = "DATE")]
    public DateTime WriteOffDate { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItsId { get; set; }

    /// <summary>CPF/CNPJ usado na baixa do crédito — digitado, pode não ser o parceiro do romaneio.</summary>
    [Column(TypeName = "VARCHAR(18) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Situação comum a todas as linhas (no ITS cada situação é uma operação diferente).</summary>
    public RoyaltySituation Situation { get; set; }

    public RoyaltyWriteOffStatus Status { get; set; } = RoyaltyWriteOffStatus.Active;

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public ICollection<RoyaltyWriteOffLine> Lines { get; set; } = [];
}
