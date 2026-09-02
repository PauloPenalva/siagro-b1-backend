using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Origem do documento de saída — a regra que decide qual ramo de processamento o documento
/// segue. Fonte ÚNICA: nenhum serviço deve re-derivar isso, sob pena de as duas cópias
/// divergirem em silêncio.
/// </summary>
/// <remarks>
/// <b>Ordem importa.</b> Um documento de carga tem <c>SalesTransactions</c> VAZIA — ele não
/// conhece romaneio, chega neles pela carga. Testar a coleção primeiro (que era o que o código
/// fazia antes da Carga existir) manda o documento de carga para o ramo AVULSO, onde a
/// alocação vira ajuste fiscal em vez de faturamento e corrompe o saldo do contrato.
/// <para>
/// <see cref="Resolve"/> depende de <c>SalesTransactions</c> CARREGADA. Quem não faz o
/// <c>.Include</c> — <c>SalesInvoicesDeleteService</c> é o caso — deve usar só
/// <see cref="IsShipmentLoad"/>, que lê o escalar e não navega.
/// </para>
/// </remarks>
public static class SalesInvoiceOriginResolver
{
    public static SalesInvoiceOrigin Resolve(SalesInvoice invoice) =>
        invoice.ShipmentLoadKey is not null ? SalesInvoiceOrigin.ShipmentLoad
        : invoice.SalesTransactions.Count > 0 ? SalesInvoiceOrigin.LegacyShipment
        : SalesInvoiceOrigin.Standalone;

    /// <summary>
    /// Discriminador que NÃO navega: seguro onde <c>SalesTransactions</c> não foi incluída.
    /// </summary>
    public static bool IsShipmentLoad(SalesInvoice invoice) => invoice.ShipmentLoadKey is not null;

    /// <summary>
    /// Documento que consome romaneio — por carga ou pelo caminho legado. É a condição dos
    /// dois pontos que antes perguntavam <c>SalesTransactions.Count > 0</c>.
    /// </summary>
    public static bool ConsumesShipments(SalesInvoice invoice) =>
        Resolve(invoice) is SalesInvoiceOrigin.ShipmentLoad or SalesInvoiceOrigin.LegacyShipment;

    /// <summary>
    /// A carga que o documento afeta. Normalmente é a própria: <c>SalesInvoiceCopyFactory</c>
    /// copia <c>ShipmentLoadKey</c> para a devolução, de modo que retorno e origem apontem a
    /// mesma carga. O fallback pela origem cobre devoluções criadas ANTES dessa cópia existir.
    /// </summary>
    /// <remarks>
    /// Fonte única do "de qual carga é este documento?" — antes vivia duplicada em
    /// <c>ShipmentLoadsBalanceHookService.ResolveLoadKeyAsync</c>, e o estorno de confirmação
    /// precisava da mesma pergunta para não cair no ramo legado.
    /// </remarks>
    public static async Task<Guid?> ResolveShipmentLoadKeyAsync(AppDbContext context, SalesInvoice invoice)
    {
        if (invoice.ShipmentLoadKey is { } own)
            return own;

        if (invoice.InvoiceType != SalesInvoiceType.Return || invoice.SalesInvoiceOriginKey == null)
            return null;

        return await context.SalesInvoices
            .Where(x => x.Key == invoice.SalesInvoiceOriginKey.Value)
            .Select(x => x.ShipmentLoadKey)
            .FirstOrDefaultAsync();
    }
}
