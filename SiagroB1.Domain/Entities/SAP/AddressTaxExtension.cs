using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Extensão fiscal do endereço do parceiro no SAP Business One (CRD7). É aqui que
/// moram CNPJ, CPF e Inscrição Estadual — uma linha por endereço, e não no OCRD.
/// </summary>
/// <remarks>
/// Atenção à grafia: em CRD7 a coluna do tipo de endereço é <c>AddrType</c>, enquanto
/// em CRD1 (<see cref="Address"/>) a coluna equivalente é <c>AdresType</c>.
/// </remarks>
[Table("CRD7")]
public class AddressTaxExtension
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column("Address", TypeName = "VARCHAR(50) NOT NULL")]
    public required string AddressName { get; set; }

    /// <summary>"S" = entrega, "B" = cobrança.</summary>
    [Column("AddrType", TypeName = "VARCHAR(1) NOT NULL")]
    public required string AddressType { get; set; }

    [Column("TaxId0")]
    public string? Cnpj { get; set; }

    [Column("TaxId1")]
    public string? StateRegistration { get; set; }

    [Column("TaxId4")]
    public string? Cpf { get; set; }
}
