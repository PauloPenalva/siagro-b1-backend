namespace SiagroB1.Security.Dtos;

/// <summary>Item do seletor da tela de login. Só código e nome: o endpoint é anônimo.</summary>
public class CompanyOption
{
    public string Code { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
}
