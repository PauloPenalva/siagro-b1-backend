namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Uma CC-e registrada na SEFAZ (evento 110110) — o que as tabelas de cartas do documento de saída e do de
/// entrada têm em comum. Cada carta substitui a anterior na SEFAZ; aqui todas ficam (histórico e escrituração).
/// </summary>
public interface INfeCorrection
{
    Guid Key { get; }
    int Sequence { get; }
    string Text { get; }
    string? Protocol { get; }
    DateTime? RegisteredAt { get; }
    int StatusCode { get; }
    string Reason { get; }
    string? ProcEventXml { get; }
    DateTime CreatedAt { get; }
    string? CreatedBy { get; }
}
