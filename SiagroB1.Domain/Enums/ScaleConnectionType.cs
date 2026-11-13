namespace SiagroB1.Domain.Enums;

/// <summary>Como o Client fala com o indicador da balança.</summary>
public enum ScaleConnectionType
{
    Tcp,     // Indicador (ou conversor serial-ethernet) em rede: IP e porta
    Serial   // Indicador ligado direto na porta serial do PC da balança
}
