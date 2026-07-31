namespace SiagroB1.Security.Dtos;

/// <summary>Dados que o próprio usuário vê e mantém na tela "Meu Perfil".</summary>
public class UserProfileDto
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public string? Theme { get; set; }

    /// <summary>A imagem em si vem do endpoint da foto - aqui só a informação de que existe.</summary>
    public bool HasPhoto { get; set; }

    /// <summary>
    /// Regra de senha vigente, em texto. Vem do servidor porque a política é configurável por
    /// ambiente: um aviso fixo na tela viraria mentira assim que ela mudasse.
    /// </summary>
    public string PasswordRequirements { get; set; } = string.Empty;
}

/// <summary>Resultado simples de uma alteração de perfil.</summary>
public record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}

public class SetThemeRequest
{
    public string Theme { get; set; } = string.Empty;
}

public class SetPhotoRequest
{
    public string? ContentType { get; set; }

    /// <summary>Conteúdo da imagem em base64, sem o prefixo <c>data:...;base64,</c>.</summary>
    public string? File { get; set; }
}
