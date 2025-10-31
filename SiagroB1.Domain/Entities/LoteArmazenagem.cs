using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("lote_armazenagem")]
public class LoteArmazenagem : BaseEntity<int>
{
    [Column("data_lote")]
    public required DateOnly DataLote { get; set; }

    [Column("descricao")]
    public required string Descricao { get; set; }

    [Column("participante_id")]
    [ForeignKey("ParceiroNegocio")]
    public required int ParceiroNegocioId { get; set; }
    public virtual Participante? ParceiroNegocio { get; set; }
    
    [Column("produto_id")]
    [ForeignKey("Produto")]
    public required int ProdutoId { get; set; }
    public virtual Produto? Produto { get; set; }

    [Column("tabela_id")]
    [ForeignKey("TabelaCusto")]
    public int TabelaCustoId { get; set; }
    public virtual TabelaCusto? TabelaCusto { get; set; }
    
    [Column("armazem_id")]
    [ForeignKey("Armazem")]
    public int ArmazemId { get; set; }
    public virtual Armazem? Armazem { get; set; }
    
    [Column("participante_cobranca_id")]
    [ForeignKey("ParceiroNegocioCobranca")]
    public required int ParceiroNegocioCobrancaId { get; set; }
    public virtual Participante? ParceiroNegocioCobranca { get; set; }
    
    [Column("participante_faturamento_id")]
    [ForeignKey("ParceiroNegocioFaturamento")]
    public required int ParceiroNegocioFaturamentoId { get; set; }
    public virtual Participante? ParceiroNegocioFaturamento { get; set; }

    [Column("encerrado")]
    public bool Encerrado { get; set; } =  false;
    
    [Column("saldo")]
    public decimal Saldo { get; set; } = 0;

    [Column("unidade_medida_codigo")]
    public string UnidadeMedida { get; set; } = "KG";
}