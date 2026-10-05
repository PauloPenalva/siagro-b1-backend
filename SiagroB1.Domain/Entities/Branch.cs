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
        // --- Emitente da NF-e STANDALONE (ocultos e não validados em SAPB1). ---

        /// <summary>Razão social do emitente (<c>emit/xNome</c>). O <see cref="BranchName"/> é rótulo interno.</summary>
        [Column(TypeName = "VARCHAR(60)")]
        public string? LegalName { get; set; }

        /// <summary>Nome fantasia (<c>xFant</c>).</summary>
        [Column(TypeName = "VARCHAR(60)")]
        public string? TradeName { get; set; }

        /// <summary>Inscrição estadual (<c>IE</c>).</summary>
        [Column(TypeName = "VARCHAR(14)")]
        public string? StateRegistration { get; set; }

        [Column(TypeName = "VARCHAR(60)")]
        public string? Street { get; set; }

        [Column(TypeName = "VARCHAR(60)")]
        public string? StreetNumber { get; set; }

        [Column(TypeName = "VARCHAR(60)")]
        public string? Complement { get; set; }

        /// <summary>Bairro (<c>xBairro</c>).</summary>
        [Column(TypeName = "VARCHAR(60)")]
        public string? District { get; set; }

        /// <summary>Município do IBGE: dá o <c>cMun</c>/<c>xMun</c> e, nos 2 primeiros dígitos, o <c>cUF</c>.</summary>
        [Column(TypeName = "VARCHAR(7)")]
        [ForeignKey(nameof(Municipality))]
        public string? MunicipalityCode { get; set; }

        public virtual Municipality? Municipality { get; set; }

        /// <summary>CEP, 8 dígitos sem máscara.</summary>
        [Column(TypeName = "VARCHAR(8)")]
        public string? ZipCode { get; set; }

        [Column(TypeName = "VARCHAR(14)")]
        public string? Phone { get; set; }
    }
}