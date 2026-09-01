using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("CRD1")]
public class Address
{
   
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column("Address", TypeName = "VARCHAR(15) NOT NULL")]
    public required string AddressName { get; set; }
    
    [Column(TypeName = "VARCHAR(1) NOT NULL")]
    public required string AdresType { get; set; }
    
    /// <summary>Tipo de logradouro (RUA, AVENIDA...), coluna AddrType de CRD1.</summary>
    [Column("AddrType")]
    public string? StreetType { get; set; }

    public string? Street { get; set; }

    public string? StreetNo { get; set; }

    /// <summary>
    /// Chave para OCNT (AbsId). Sem FK: OCNT é somente leitura. É string de propósito —
    /// no banco a coluna é varchar e guarda o AbsId como texto; mapear como int faz o EF
    /// estourar InvalidCastException ao ler a coleção de endereços.
    /// </summary>
    public string? County { get; set; }

    public string? Block { get; set; }
    
    public string? ZipCode { get; set; }
    
    public string? City { get; set; }
    
    public string? State { get; set; }
    
    public string? Country { get; set; }
    
    public virtual BusinessPartner? BusinessPartner { get; set; }
}