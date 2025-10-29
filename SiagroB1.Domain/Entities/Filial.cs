using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Entities
{
    [Table("filial")]
    public class Filial
    {
        [Key]
        [Column(name: "id", Order = 1)]
        [JsonPropertyOrder(1)]
        public int Id { get; set; }

        [Column("cod_filial")]
        public required string CodFilial { get; set; }

        [Column("razao_social")]
        public required string RazaoSocial { get; set; }

        [Column("nome_fantasia")]
        public required string NomeFantasia { get; set; }
        
         [Column(name: "chave_integracao", Order = 3)]
        [JsonPropertyOrder(2)]
        public string? ChaveIntegracao { get; set; }
    }
}