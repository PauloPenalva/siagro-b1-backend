namespace SiagroB1.Security.Dtos;

public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime? LastLogin { get; set; }

    /// <summary>Tema escolhido pelo usuário. Nulo usa o tema padrão do bootstrap.</summary>
    public string? Theme { get; set; }

    /// <summary>Diz à shell se deve pedir a imagem do avatar ou mostrar as iniciais.</summary>
    public bool HasPhoto { get; set; }
}