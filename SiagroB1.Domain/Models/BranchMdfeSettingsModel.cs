using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>O que a tela "Configuração do MDF-e" vê e grava.</summary>
public class BranchMdfeSettingsModel
{
    public string BranchCode { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public NfeEnvironment Environment { get; set; } = NfeEnvironment.Homologation;
    public int DefaultSeries { get; set; } = 1;
    public string DefaultCargoType { get; set; } = "02";
    public bool RequiresCiot { get; set; }
    public List<BranchMdfeSeriesModel> SeriesList { get; set; } = [];
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Uma série do MDF-e da filial.</summary>
public class BranchMdfeSeriesModel
{
    public int Series { get; set; }
    public string Description { get; set; } = string.Empty;
    public int NextNumber { get; set; }

    /// <summary>Série que já emitiu (próximo número acima de 1): não se exclui.</summary>
    public bool IsUsed { get; set; }
}
