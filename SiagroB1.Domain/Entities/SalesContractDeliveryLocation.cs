using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um local de entrega do contrato de VENDA: aponta para o cadastro de clientes
/// (<see cref="BusinessPartner"/> com CardType 'C'). Relação 1:N com o contrato —
/// um contrato pode entregar em vários terminais/portos conforme a cota disponível.
/// </summary>
[Table("SALES_CONTRACTS_DELIVERY_LOCATIONS")]
public class SalesContractDeliveryLocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>
    /// SAP ENTITY (cliente). Sem propriedade de navegação para BusinessPartner:
    /// em modo SAPB1 a tabela local BUSINESS_PARTNERS está vazia — o INNER JOIN
    /// zeraria a coleção. O nome é desnormalizado na gravação.
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }
}
