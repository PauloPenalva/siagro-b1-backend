using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.StorageCustody;

/// <summary>
/// Guarda das notas de custódia em armazém próprio (spec 2026-10-12 §4). Recebe TODAS as linhas que o documento terá
/// depois da operação, decide o papel pela natureza delas e o grava em <c>CustodyRole</c> — o corpo da API nunca o
/// define. Plantada na gravação (create/update/linhas), na confirmação, na reserva do número da NF-e, no cancelamento,
/// na exclusão e nos estornos de romaneio/transferência. Fora do STANDALONE nenhuma natureza tem papel (o cadastro SAP
/// recusa), então a guarda é inerte lá.
/// </summary>
public static class StorageCustodyGuard
{
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static string Label(StorageCustodyRole role) =>
        StorageCustodyRules.RoleText(role).ToLower(PtBr);

    private static async Task<StorageCustodyRole> DocumentRoleAsync(AppDbContext ctx, IReadOnlyList<int?> usageCodes)
    {
        var known = usageCodes.Where(c => c != null).Select(c => c!.Value).Distinct().ToList();
        var roles = known.Count == 0
            ? new Dictionary<int, StorageCustodyRole>()
            : await ctx.UsageEffects.AsNoTracking()
                .Where(e => known.Contains(e.UsageCode))
                .ToDictionaryAsync(e => e.UsageCode, e => e.StorageCustodyRole);

        var distinct = usageCodes
            .Select(c => c is { } code ? roles.GetValueOrDefault(code) : StorageCustodyRole.None)
            .Distinct()
            .ToList();

        if (distinct.Count > 1)
            throw new DefaultException("Nota de custódia não mistura naturezas: todas as linhas precisam ter o mesmo papel.");

        return distinct.SingleOrDefault();
    }

    public static async Task EnsurePurchaseAsync(AppDbContext ctx, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        var role = await DocumentRoleAsync(ctx, lines.Select(l => l.UsageCode).ToList());
        invoice.CustodyRole = role;

        if (role == StorageCustodyRole.None)
        {
            EnsureNoDanglingRomaneio(invoice.CustodyStorageTransactionKey);
            return;
        }

        if (!StorageCustodyRules.IsPurchaseRole(role))
            throw new DefaultException($"Natureza de {Label(role)} é de saída: gere a nota pelo romaneio (Notas fiscais de custódia).");

        if (invoice.InvoiceType != PurchaseInvoiceType.Normal)
            throw new DefaultException("Nota de custódia precisa ser do tipo Normal.");

        await EnsureCoreAsync(ctx, role, invoice.CustodyStorageTransactionKey, invoice.Key, invoice.CardCode, invoice.BranchCode,
            lines.Select(l => l.ItemCode).ToList(), lines.Sum(l => l.Quantity),
            checkQuantity: invoice.IssuerType == DocumentIssuerType.Own);
    }

    public static async Task EnsureSalesAsync(AppDbContext ctx, SalesInvoice invoice, IReadOnlyList<SalesInvoiceItem> lines)
    {
        var role = await DocumentRoleAsync(ctx, lines.Select(l => l.UsageCode).ToList());
        invoice.CustodyRole = role;

        if (role == StorageCustodyRole.None)
        {
            EnsureNoDanglingRomaneio(invoice.CustodyStorageTransactionKey);
            return;
        }

        if (!StorageCustodyRules.IsSalesRole(role))
            throw new DefaultException($"Natureza de {Label(role)} é de entrada: lance no Documento de Entrada.");

        if (invoice.CustodyStorageTransactionKey is null)
            throw new DefaultException($"Natureza de {Label(role)}: gere a nota pelo romaneio (Notas fiscais de custódia).");

        if (invoice.InvoiceType != SalesInvoiceType.Normal)
            throw new DefaultException("Nota de custódia precisa ser do tipo Normal.");

        await EnsureCoreAsync(ctx, role, invoice.CustodyStorageTransactionKey, invoice.Key, invoice.CardCode, invoice.BranchCode,
            lines.Select(l => l.ItemCode).ToList(), lines.Sum(l => l.Quantity), checkQuantity: true);
    }

    private static void EnsureNoDanglingRomaneio(Guid? transactionKey)
    {
        if (transactionKey != null)
            throw new DefaultException("O documento aponta um romaneio de armazenagem, mas nenhuma linha tem natureza de custódia.");
    }

    private static async Task EnsureCoreAsync(
        AppDbContext ctx, StorageCustodyRole role, Guid? transactionKey, Guid documentKey, string? cardCode,
        string? branchCode, IReadOnlyList<string?> itemCodes, decimal quantity, bool checkQuantity)
    {
        if (transactionKey is not { } key)
            throw new DefaultException($"A nota de {Label(role)} precisa indicar o romaneio do lote de armazenagem.");

        var transaction = await StorageCustodyQuery.Transactions(ctx).FirstOrDefaultAsync(t => t.Key == key)
                          ?? throw new DefaultException("O romaneio indicado não é de lote de armazenagem de terceiro.");

        var expected = StorageCustodyRules.ExpectedRoles(transaction);
        if (!expected.Contains(role))
            throw new DefaultException($"O romaneio {transaction.Code} não espera a nota de {Label(role)}.");

        if (cardCode != transaction.CardCode)
            throw new DefaultException($"A nota de custódia precisa ser do depositante do lote ({transaction.CardCode}).");

        if (itemCodes.Any(c => c != transaction.ItemCode))
            throw new DefaultException($"A nota de custódia precisa ser do produto do lote ({transaction.ItemCode}).");

        if (branchCode != transaction.BranchCode)
            throw new DefaultException($"A nota de custódia precisa ser da filial do lote ({transaction.BranchCode}).");

        var others = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, [key]))
            .Where(d => d.DocumentKey != documentKey)
            .ToList();

        if (others.FirstOrDefault(d => d.Role == role) is { } same)
            throw new DefaultException(
                $"O romaneio {transaction.Code} já tem a nota de {Label(role)} nº {StorageCustodyRules.DocumentNumber(same)} ativa.");

        if (role == StorageCustodyRole.ProcessingSymbolicReturn)
        {
            var entry = others.FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry);
            if (entry is null || !HasAccessKey(entry))
                throw new DefaultException(
                    "A devolução simbólica exige a entrada para beneficiamento com chave de acesso (NF-e própria autorizada ou escrituração confirmada).");

            if (quantity != entry.Quantity)
                throw new DefaultException(
                    $"A devolução simbólica precisa ter a quantidade da entrada para beneficiamento ({entry.Quantity.ToString("N3", PtBr)}).");

            return;
        }

        if (role == StorageCustodyRole.StorageEntry
            && expected.Contains(StorageCustodyRole.ProcessingSymbolicReturn)
            && others.All(d => d.Role != StorageCustodyRole.ProcessingSymbolicReturn))
            throw new DefaultException("Gere antes a devolução simbólica do beneficiamento deste romaneio.");

        if (checkQuantity)
        {
            var expectedQuantity = StorageCustodyRules.ExpectedQuantity(role, transaction, null)!.Value;
            if (quantity != expectedQuantity)
                throw new DefaultException(
                    $"A nota de {Label(role)} precisa ter a quantidade do romaneio {transaction.Code} ({expectedQuantity.ToString("N3", PtBr)}).");
        }
    }

    /// <summary>Chave de 44 dígitos que a SEFAZ conhece: NF-e própria autorizada ou escrituração confirmada.</summary>
    public static bool HasAccessKey(StorageCustodyDocumentRow d) =>
        d.ChaveNFe is { Length: 44 } k && k.All(char.IsAsciiDigit)
        && (d.IssuerType == DocumentIssuerType.Own ? d.NfeStatus == NfeStatus.Authorized : d.InvoiceStatus == InvoiceStatus.Confirmed);

    /// <summary>NFref da devolução simbólica: a chave da entrada para beneficiamento do mesmo romaneio.</summary>
    public static async Task<string?> ProcessingEntryAccessKeyAsync(AppDbContext ctx, SalesInvoice invoice)
    {
        if (invoice.CustodyRole != StorageCustodyRole.ProcessingSymbolicReturn || invoice.CustodyStorageTransactionKey is not { } key)
            return null;

        var entry = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, [key]))
                    .FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry && HasAccessKey(d))
                    ?? throw new DefaultException("A devolução simbólica está sem a entrada para beneficiamento com chave de acesso.");

        // A quantidade pode ter mudado entre a gravação e a emissão: a conferência da guarda vale na hora de transmitir.
        var quantity = invoice.Items.Sum(i => i.Quantity);
        if (quantity != entry.Quantity)
            throw new DefaultException(
                $"A devolução simbólica precisa ter a quantidade da entrada para beneficiamento ({entry.Quantity.ToString("N3", PtBr)}).");

        return entry.ChaveNFe;
    }


    /// <summary>Etapa que depende desta: cancelar/excluir esta exige cancelar aquela antes (spec §4.5).</summary>
    private static StorageCustodyRole LaterStep(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => StorageCustodyRole.ProcessingSymbolicReturn,
        StorageCustodyRole.ProcessingSymbolicReturn => StorageCustodyRole.StorageEntry,
        _ => StorageCustodyRole.None,
    };

    private static async Task EnsureNoLaterStepAsync(AppDbContext ctx, Guid? transactionKey, StorageCustodyRole role, Guid documentKey)
    {
        var later = LaterStep(role);
        if (transactionKey is not { } key || later == StorageCustodyRole.None)
            return;

        var hit = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, [key]))
            .FirstOrDefault(d => d.Role == later && d.DocumentKey != documentKey);

        if (hit != null)
            throw new DefaultException(
                $"Cancele primeiro a nota de {Label(later)} nº {StorageCustodyRules.DocumentNumber(hit)} deste romaneio.");
    }

    public static async Task EnsurePurchaseHasNoLaterStepAsync(AppDbContext ctx, Guid purchaseInvoiceKey)
    {
        var doc = await ctx.PurchaseInvoices.AsNoTracking()
            .Where(i => i.Key == purchaseInvoiceKey)
            .Select(i => new { i.CustodyStorageTransactionKey, i.CustodyRole })
            .FirstOrDefaultAsync();

        if (doc != null)
            await EnsureNoLaterStepAsync(ctx, doc.CustodyStorageTransactionKey, doc.CustodyRole, purchaseInvoiceKey);
    }

    public static async Task EnsureSalesHasNoLaterStepAsync(AppDbContext ctx, Guid salesInvoiceKey)
    {
        var doc = await ctx.SalesInvoices.AsNoTracking()
            .Where(i => i.Key == salesInvoiceKey)
            .Select(i => new { i.CustodyStorageTransactionKey, i.CustodyRole })
            .FirstOrDefaultAsync();

        if (doc != null)
            await EnsureNoLaterStepAsync(ctx, doc.CustodyStorageTransactionKey, doc.CustodyRole, salesInvoiceKey);
    }

    /// <summary>
    /// Romaneio (ou transferência) com nota de custódia ativa não é cancelado nem estornado: a nota ficaria apontando um
    /// movimento que não existe mais. ApplicationException, como as travas vizinhas desses serviços.
    /// </summary>
    public static async Task EnsureTransactionsHaveNoCustodyAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        var hit = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, transactionKeys)).FirstOrDefault();

        if (hit != null)
            throw new ApplicationException(
                $"O romaneio tem nota fiscal de custódia ativa (nº {StorageCustodyRules.DocumentNumber(hit)}). " +
                "Cancele as notas fiscais de custódia antes de estornar.");
    }
}
