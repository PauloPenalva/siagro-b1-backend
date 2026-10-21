using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Parâmetros da empresa. Uma única linha por banco (Id sempre 1).</summary>
[Table("COMPANY_SETTINGS")]
public class CompanySettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = 1;

    /// <summary>Exige <c>Dim1Code</c> ao confirmar/criar o documento.</summary>
    public bool RequireCostCenter { get; set; }

    /// <summary>Exige <c>LedgerAccountCode</c> ao confirmar/criar o documento.</summary>
    public bool RequireLedgerAccount { get; set; }
}
