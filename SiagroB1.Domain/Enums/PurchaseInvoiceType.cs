namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo do documento de entrada. Normal e Devolução, simétricos a <see cref="SalesInvoiceType"/>, o Complemento de valor (spec 2026-10-13 D11) e o
/// Complemento de ICMS (spec 2026-10-17).
///
/// Compra de mercadoria para comercialização, faturamento antecipado de venda futura e remessa por
/// carregamento NÃO são tipos: são NATUREZAS DE OPERAÇÃO (<c>Usage</c> + <c>UsageEffect</c>),
/// configuradas em cadastro. É o que faz fluxo fiscal novo não exigir enum nem migration.
/// </summary>
public enum PurchaseInvoiceType
{
    Normal = 0,
    Return = 1,

    /// <summary>
    /// Complemento de VALOR (spec 2026-10-13 D11): o produtor faturou abaixo do contrato e emite uma NF só de valor.
    /// Linhas sem quantidade (PurchaseInvoiceItem.ComplementAmount), contrato e natureza obrigatórios; não se concilia
    /// com romaneio; o título firme nasce na confirmação. Só escrituração (terceiro) — a emissão própria é o sub-projeto 2b.
    /// </summary>
    ValueComplement = 2,

    /// <summary>
    /// Complemento de ICMS (spec 2026-10-17 D1): NF só de imposto — linhas sem quantidade, preço nem valor, com a base e o
    /// valor do ICMS informados pelo operador. Próprio (NF-e complementar) e de terceiro; só STANDALONE (K3). Não gera
    /// título (D5) e não se concilia com romaneio.
    /// </summary>
    IcmsComplement = 3,
}
