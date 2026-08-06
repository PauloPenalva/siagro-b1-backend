namespace SiagroB1.Domain.Enums;

/// <summary>
/// Quem emitiu o documento fiscal.
///
/// <c>ThirdParty</c> é o caso normal da entrada — fornecedor, produtor rural ou cliente devolvendo:
/// o número vem do emitente e não consome sequência própria. <c>Own</c> é a emissão própria, em que
/// o número é digitado pelo operador nesta fase e passa a ser numerado automaticamente pelo
/// <c>DocNumbers</c> na Fase 3.
/// </summary>
public enum DocumentIssuerType
{
    ThirdParty = 0,
    Own = 1,
}
