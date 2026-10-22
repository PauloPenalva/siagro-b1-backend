using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>O que a tela "Configuração do CT-e" vê e grava.</summary>
public class BranchCteSettingsModel
{
    public string BranchCode { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public NfeEnvironment Environment { get; set; } = NfeEnvironment.Homologation;
    public int DefaultSeries { get; set; } = 1;
    public string DefaultQuantityUnit { get; set; } = "00";
    public List<BranchCteSeriesModel> SeriesList { get; set; } = [];
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Uma série do CT-e da filial.</summary>
public class BranchCteSeriesModel
{
    public int Series { get; set; }
    public string Description { get; set; } = string.Empty;
    public int NextNumber { get; set; }

    /// <summary>Série que já emitiu (próximo número acima de 1): não se exclui.</summary>
    public bool IsUsed { get; set; }
}
