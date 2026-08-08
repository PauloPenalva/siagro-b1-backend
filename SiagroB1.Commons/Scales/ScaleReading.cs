namespace SiagroB1.Commons.Scales;

/// <summary>Uma leitura crua do indicador, em quilos inteiros.</summary>
public sealed record ScaleReading(int Weight, DateTime Timestamp);

/// <summary>O que a tela precisa saber sobre a balança neste instante.</summary>
public sealed record LiveWeight(int Weight, bool Stable, bool Online);
