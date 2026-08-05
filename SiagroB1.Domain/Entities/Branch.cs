using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("BRANCHS")]
    public class Branch
    {
        [Key]
        [Column(TypeName = "VARCHAR(14) NOT NULL", Order = 1)]
        public string? Code { get; set; }

        [Column(TypeName = "VARCHAR(100) NOT NULL")]
        public required string BranchName { get; set; }
        
        [Column(TypeName = "VARCHAR(50) NOT NULL")]
        public string? ShortName { get; set; }
        
        [Column(TypeName = "VARCHAR(14) NOT NULL")]
        public string? TaxId { get; set; }

        /// <summary>
        /// UF da filial, sem FK para STATES — coerente com o restante do cadastro.
        /// É o lado esquerdo da comparação que decide entre CFOP dentro e fora do estado.
        ///
        /// Nulável porque as filiais existentes não têm o dado e não há backfill possível
        /// (a UF não é derivável de nada que já esteja gravado). A resolução do CFOP trata
        /// a ausência como erro de negócio explícito, nunca como silêncio.
        /// </summary>
        [Column(TypeName = "VARCHAR(2)")]
        public string? StateCode { get; set; }
    }
}