namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Dimensões gerenciais (centros de custo / regras de distribuição) e conta contábil de um
/// documento. Sem FK: o cadastro é dual-mode e a validação é feita por serviço.
/// </summary>
public interface IDimensionFields
{
    string? Dim1Code { get; set; }
    string? Dim2Code { get; set; }
    string? Dim3Code { get; set; }
    string? Dim4Code { get; set; }
    string? Dim5Code { get; set; }
    string? LedgerAccountCode { get; set; }
}
