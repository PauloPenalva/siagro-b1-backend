using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Municípios do SAP Business One (OCNT). O endereço em CRD1 aponta para cá pelo
/// campo County; o nome daqui tem precedência sobre o texto livre de CRD1.City.
/// </summary>
[Table("OCNT")]
public class County
{
    [Key]
    public int AbsId { get; set; }

    public string? Name { get; set; }

    public string? State { get; set; }
}
