namespace SiagroB1.Commons.Scales;

public interface IScaleProtocol
{
    /// <summary>Converte um frame já delimitado em quilos inteiros. Devolve false para lixo.</summary>
    bool TryParse(string frame, out int weightKg);
}
