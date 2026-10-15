using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>Resultado do backlog dos firmes a receber (spec 2026-10-15, regra 19).</summary>
public class SalesFirmBacklogResultDto
{
    [JsonPropertyName("DryRun")] public bool DryRun { get; set; }

    /// <summary>Linhas confirmadas da janela, de contrato aprovado.</summary>
    [JsonPropertyName("EligibleLines")] public int EligibleLines { get; set; }

    /// <summary>Elegíveis sem nenhum título ativo — as que a execução sincroniza.</summary>
    [JsonPropertyName("LinesWithoutFirm")] public int LinesWithoutFirm { get; set; }

    /// <summary>Títulos criados (uma linha realocada gera mais de um).</summary>
    [JsonPropertyName("Generated")] public int Generated { get; set; }

    [JsonPropertyName("SkippedAlreadyGenerated")] public int SkippedAlreadyGenerated { get; set; }
}
