namespace SiagroB1.Domain.Interfaces;

public static class IDimensionFieldsExtensions
{
    /// <summary>Lê a dimensão de número 1 a 5.</summary>
    public static string? GetDim(this IDimensionFields f, int n) => n switch
    {
        1 => f.Dim1Code,
        2 => f.Dim2Code,
        3 => f.Dim3Code,
        4 => f.Dim4Code,
        5 => f.Dim5Code,
        _ => throw new ArgumentOutOfRangeException(nameof(n), n, "Dimensão deve ser de 1 a 5."),
    };

    /// <summary>Grava a dimensão de número 1 a 5.</summary>
    public static void SetDim(this IDimensionFields f, int n, string? v)
    {
        switch (n)
        {
            case 1: f.Dim1Code = v; break;
            case 2: f.Dim2Code = v; break;
            case 3: f.Dim3Code = v; break;
            case 4: f.Dim4Code = v; break;
            case 5: f.Dim5Code = v; break;
            default: throw new ArgumentOutOfRangeException(nameof(n), n, "Dimensão deve ser de 1 a 5.");
        }
    }
}
