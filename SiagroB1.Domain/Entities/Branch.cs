using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

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

        /// <summary>
        /// Regime tributário (CRT) da NF-e. Decide CST (CRT 2/3) ou CSOSN (CRT 1/4) na linha.
        /// Nulável: as filiais existentes não têm o dado, e só a emissão de NF-e o exige.
        /// </summary>
        public TaxRegime? TaxRegime { get; set; }

        /// <summary>
        /// "Emite NF-e pelo Siagro". Junto com Erp=STANDALONE é o que liga o cálculo de tributos
        /// e a trava da linha do documento de saída — ver <c>TaxCalculationGate</c>. Em SAPB1 é
        /// oculta e ignorada.
        /// </summary>
        public bool IssuesNfe { get; set; }
    }
}