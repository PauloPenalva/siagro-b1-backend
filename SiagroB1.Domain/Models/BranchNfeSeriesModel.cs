namespace SiagroB1.Domain.Models;

/// <summary>Uma série da NF-e da filial, como a tela "Configuração da NF-e" a vê.</summary>
public class BranchNfeSeriesModel
{
    public int Series { get; set; }
    public string Description { get; set; } = string.Empty;
    public int NextNumber { get; set; }

    /// <summary>Já apareceu em uma NF-e própria da filial? Série usada não se exclui.</summary>
    public bool IsUsed { get; set; }
}

/// <summary>Série enviada pela tela ao gravar a Configuração da NF-e.</summary>
public record BranchNfeSeriesInput(int Series, string Description, int NextNumber);
