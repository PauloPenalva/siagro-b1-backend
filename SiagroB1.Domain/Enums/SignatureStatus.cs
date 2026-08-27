namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da assinatura do contrato (compra ou venda). É um fato DOCUMENTAL, paralelo e
/// independente de <see cref="ContractStatus"/>: pode ser alterado a qualquer tempo, inclusive
/// em contrato já encerrado ou cancelado.
///
/// Nulo é valor legítimo — significa "não informado". Contratos anteriores à feature nascem
/// nulos e continuam assim até que alguém marque.
/// </summary>
public enum SignatureStatus
{
    AwaitingSignature = 0,
    Signed = 1,
}
