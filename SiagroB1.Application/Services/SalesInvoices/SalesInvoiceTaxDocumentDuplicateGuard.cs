using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Trava de duplicidade da nota fiscal no documento de saída, usada por quem grava número,
/// série e chave: informar a NF à mão e vincular a NF do SAP.
///
/// Documentos Cancelados e Retornados não bloqueiam a nota (vale também para a chave):
/// o cancelado porque a operação não existiu; o retornado (GAC-1194) porque a mercadoria
/// recusada volta a ser entregue com a mesma nota — ele guarda a nota só como evidência.
/// </summary>
public class SalesInvoiceTaxDocumentDuplicateGuard(AppDbContext context)
{
    /// <summary>
    /// Recusa com <see cref="ApplicationException"/> se outro documento vivo já usa o número +
    /// série (na mesma filial) ou a chave de acesso (em qualquer filial). Valores nulos não
    /// colidem. <paramref name="ignoreInvoiceKey"/> é o documento em edição, que não conta.
    /// </summary>
    public async Task EnsureUniqueAsync(
        string branchCode, string? number, string? series, string? accessKey, Guid? ignoreInvoiceKey)
    {
        var taxDocumentConflict = await FindTaxDocumentConflictAsync(
            branchCode, number, series, ignoreInvoiceKey);
        if (taxDocumentConflict is not null)
            throw new ApplicationException(
                $"Nota fiscal {number}, série {series} já informada no documento de saída {taxDocumentConflict.InvoiceNumber}.");

        var chaveConflict = await FindAccessKeyConflictAsync(accessKey, ignoreInvoiceKey);
        if (chaveConflict is not null)
            throw new ApplicationException(
                $"Chave de acesso {accessKey} já informada no documento de saída {chaveConflict.InvoiceNumber}.");
    }

    /// <summary>
    /// A chave da NF-e é única nacionalmente por construção, então a busca é global — sem
    /// escopo de filial, ao contrário do número/série.
    /// </summary>
    private async Task<SalesInvoice?> FindAccessKeyConflictAsync(string? accessKey, Guid? ignoreInvoiceKey)
    {
        if (accessKey is null)
            return null;

        return await context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ChaveNFe == accessKey &&
                x.Key != ignoreInvoiceKey &&
                x.InvoiceStatus != InvoiceStatus.Cancelled &&
                x.InvoiceStatus != InvoiceStatus.Returned);
    }

    private async Task<SalesInvoice?> FindTaxDocumentConflictAsync(
        string branchCode, string? number, string? series, Guid? ignoreInvoiceKey)
    {
        if (number is null || series is null)
            return null;

        return await context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.BranchCode == branchCode &&
                x.TaxDocumentNumber == number &&
                x.TaxDocumentSeries == series &&
                x.Key != ignoreInvoiceKey &&
                x.InvoiceStatus != InvoiceStatus.Cancelled &&
                x.InvoiceStatus != InvoiceStatus.Returned);
    }
}
