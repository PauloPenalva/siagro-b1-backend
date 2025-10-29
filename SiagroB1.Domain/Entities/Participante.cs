using System.ComponentModel.DataAnnotations;
using SiagroB1.Domain.Base;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("participantes")]
    public class Participante : BaseEntity
    {
        [Column("loja")]
        public required string Loja { get; set; }
        
        [Display(Name = "Razão Social")]
        [Column("razao_social")]
        public required string RazaoSocial { get; set; }

        [Column("tipo_pessoa")]
        public required string TipoPessoa { get; set; }

        [Column("nome_fantasia")]
        public string? NomeFantasia { get; set; }

        [Column("cnpj")]
        public required string Cnpj { get; set; }

        [Column("inscricao_estadual")]
        public required string InscricaoEstadual { get; set; }

        [Column("tipo_cliente")]
        public required string TipoCliente { get; set; }

        [Column("logradouro")]
        public string? Logradouro { get; set; }

        [Column("complemento")]
        public string? Complemento { get; set; }

        [Column("bairro")]
        public string? Bairro { get; set; }

        [Column("cep")]
        public string? Cep { get; set; }

        [Column("estado")]
        public string? Estado { get; set; }

        [Column("codigo_cidade")]
        public string? CodigoCidade { get; set; }

        [Column("cod_pais")]
        public string? CodigoPais { get; set; }

        [Column("email")]
        public string? Email { get; set; }

        [Column("status")]
        public string? Status { get; set; }

        [Column("tipo_participante")]
        public required string TipoParticipante { get; set; }

        [Column("cpf_rural")]
        public string? CpfRural { get; set; }
        
        [Column("conta_contabil_id")]
        public int? ContaContabilId { get; set; }

        [ForeignKey("ContaContabilId")]
        public ContaContabil? ContaContabil { get; set; }

    }
}