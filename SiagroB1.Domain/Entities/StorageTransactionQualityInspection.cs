using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS")]
public class StorageTransactionQualityInspection
{
    [Key]
    public Guid? Key {  get; set; }
    
    public Guid? StorageTransactionKey { get; set; }
    public virtual StorageTransaction? StorageTransaction { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("QualityAttrib")]
    public required string QualityAttribCode { get; set; }
    public virtual QualityAttrib? QualityAttrib { get; set; }

    [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
    public decimal Value { get; set; } = 0;
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal LossValue { get; set; } = 0;

    /// <summary>
    /// % de tolerância da Tabela de Custos (<see cref="ProcessingCostQualityParameter.MaxLimitRate"/>)
    /// que valeu na confirmação do romaneio (GAC-1197). Cópia, e não leitura da tabela, para a
    /// tela mostrar o que entrou no cálculo mesmo que a tabela mude depois.
    /// </summary>
    /// <remarks>
    /// Nulo = sem tolerância: atributo fora da tabela, umidade (que trabalha por faixa), romaneio
    /// ainda não confirmado ou confirmado antes deste campo existir. Zero é tolerância de verdade.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,1)")]
    public decimal? ToleranceRate { get; set; }
}