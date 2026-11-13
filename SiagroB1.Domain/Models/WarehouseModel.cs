using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class WarehouseModel
{
    [Key]
    public required string Code { get; set; }
    
    public required string Name { get; set; }
    
    /// <summary>
    /// Brazilian CNPJ
    /// </summary>
    public string? TaxId { get; set; }
    
    public string? FName { get; set; }
    
    public string? Notes { get; set; }
    
    public string? City { get; set; }
    
    public string? State { get; set; }

    /// <summary>
    /// Cadastro inativo no SAP (GAC-1196): não pode ser ESCOLHIDO em registro novo nem numa troca,
    /// mas continua resolvendo nos documentos que já o usam. Sempre false em STANDALONE.
    /// </summary>
    public bool Inactive { get; set; }
}