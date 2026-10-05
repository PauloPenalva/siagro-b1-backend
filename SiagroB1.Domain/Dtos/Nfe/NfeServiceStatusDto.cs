namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>Resposta do "Testar comunicação" (status do serviço na SEFAZ; 107 = em operação).</summary>
public class NfeServiceStatusDto
{
    public int StatusCode { get; set; }
    public string Reason { get; set; } = string.Empty;
}
