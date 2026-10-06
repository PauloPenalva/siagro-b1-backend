namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>CC-e registrada: a tela mostra "Carta de correção nº {Sequence} registrada na SEFAZ."</summary>
public class NfeCorrectionOutcomeDto
{
    public int Sequence { get; set; }
    public string? Protocol { get; set; }
    public DateTime? RegisteredAt { get; set; }
}
