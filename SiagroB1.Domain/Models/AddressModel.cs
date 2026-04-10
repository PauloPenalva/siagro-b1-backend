namespace SiagroB1.Domain.Models;

public class AddressModel
{
    public required string AddressName { get; set; }
    
    public required string AdresType { get; set; }
    
    public string? Street { get; set; }
    
    public string? Block { get; set; }
    
    public string? ZipCode { get; set; }
    
    public string? City { get; set; }
    
    public string? State { get; set; }
    
    public string? Country { get; set; }
}