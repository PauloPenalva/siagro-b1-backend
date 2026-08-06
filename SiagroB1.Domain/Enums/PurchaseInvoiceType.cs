namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo do documento de entrada. Só dois, simétrico a <see cref="SalesInvoiceType"/>.
///
/// Compra de mercadoria para comercialização, faturamento antecipado de venda futura e remessa por
/// carregamento NÃO são tipos: são NATUREZAS DE OPERAÇÃO (<c>Usage</c> + <c>UsageEffect</c>),
/// configuradas em cadastro. É o que faz fluxo fiscal novo não exigir enum nem migration.
/// </summary>
public enum PurchaseInvoiceType
{
    Normal = 0,
    Return = 1,
}
