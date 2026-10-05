namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo do documento fiscal do Documento de Entrada (spec terceiro-chave D2). <c>Nfe</c> é o eletrônico ("tipo
/// SPED", modelo 55), com chave obrigatória e conferida; <c>Other</c> é nota de serviço, nota de papel/talão etc.,
/// sem chave. A emissão própria é sempre <c>Nfe</c>.
/// </summary>
public enum TaxDocumentKind
{
    Nfe = 0,
    Other = 1,
}
