namespace SiagroB1.Domain.Interfaces;

public interface IUoMEntity
{
    string Code { get; set; }
    
    string Description { get; set; }
    
    string? Locked {get; set;}
}