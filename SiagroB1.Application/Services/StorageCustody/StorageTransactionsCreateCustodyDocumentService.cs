using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.StorageCustody;

public sealed record StorageCustodyDocumentRequest(Guid StorageTransactionKey, StorageCustodyRole Role, int UsageCode, decimal UnitPrice);

/// <summary>
/// "Gerar nota" no romaneio (spec 2026-10-12 §4.1): cria a nota de custódia PRÓPRIA Pendente — Documento de Entrada
/// para as entradas, de Saída para a devolução simbólica e os retornos — ligada ao romaneio, para o depositante, com o
/// produto e a quantidade da etapa. Quem confere ordem, unicidade e chave é a StorageCustodyGuard, chamada pelos
/// serviços de criação. Não confirma nem transmite.
/// </summary>
public class StorageTransactionsCreateCustodyDocumentService(
    IUnitOfWork db,
    IConfiguration configuration,
    IBusinessPartnerService businessPartners,
    IUsage usages,
    PurchaseInvoicesCreateService purchaseCreate,
    SalesInvoicesCreateService salesCreate,
    ILogger<StorageTransactionsCreateCustodyDocumentService> logger)
{
    public async Task<Guid> ExecuteAsync(StorageCustodyDocumentRequest request, string userName)
    {
        // TODA a validação antes de qualquer escrita.
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageCustodyGuard.StandaloneOnlyMessage);

        var transaction = await StorageCustodyQuery.Transactions(db.Context)
                              .FirstOrDefaultAsync(t => t.Key == request.StorageTransactionKey)
                          ?? throw new NotFoundException("Romaneio de lote de armazenagem de terceiro não encontrado.");

        var label = StorageCustodyRules.RoleText(request.Role).ToLowerInvariant();

        if (!StorageCustodyRules.ExpectedRoles(transaction).Contains(request.Role))
            throw new DefaultException($"O romaneio {transaction.Code} não espera a nota de {label}.");

        var usage = await usages.GetByIdAsync(request.UsageCode);
        if (usage is null || usage.StorageCustodyRole != request.Role)
            throw new DefaultException($"Escolha uma natureza de {label}.");

        if (request.UnitPrice <= 0)
            throw new DefaultException("Informe o preço unitário da nota.");

        if (await businessPartners.GetByIdAsync(transaction.CardCode) is null)
            throw new DefaultException(
                $"O depositante {transaction.CardCode} não tem parceiro de negócio cadastrado: a nota precisa de destinatário.");

        var quantity = request.Role == StorageCustodyRole.ProcessingSymbolicReturn
            ? (await StorageCustodyQuery.ActiveDocumentsAsync(db.Context, [transaction.Key]))
                  .FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry)?.Quantity
              ?? throw new DefaultException("Gere antes a entrada para beneficiamento deste romaneio.")
            : StorageCustodyRules.ExpectedQuantity(request.Role, transaction, null)!.Value;

        var today = DateTime.Now.Date;
        var comments = $"{StorageCustodyRules.RoleText(request.Role)} — romaneio {transaction.Code}, lote {transaction.StorageAddressCode}.";

        try
        {
            await db.BeginTransactionAsync();
            Guid key;

            if (StorageCustodyRules.IsPurchaseRole(request.Role))
            {
                var invoice = new PurchaseInvoice
                {
                    Key = Guid.NewGuid(),
                    BranchCode = transaction.BranchCode,
                    CardCode = transaction.CardCode,
                    CardName = transaction.CardName,
                    IssuerType = DocumentIssuerType.Own,
                    InvoiceType = PurchaseInvoiceType.Normal,
                    TaxDocumentKind = TaxDocumentKind.Nfe,
                    IssueDate = today,
                    PostingDate = today,
                    CustodyStorageTransactionKey = transaction.Key,
                    // A emissão exige pesos; nascem iguais à quantidade e ficam editáveis enquanto Pendente.
                    GrossWeight = quantity,
                    NetWeight = quantity,
                    TruckCode = transaction.TruckCode,
                    Comments = comments,
                };
                invoice.AddItem(new PurchaseInvoiceItem
                {
                    Key = Guid.NewGuid(),
                    ItemCode = transaction.ItemCode,
                    ItemName = transaction.ItemName,
                    UnitOfMeasureCode = transaction.UoM,
                    Quantity = quantity,
                    UnitPrice = request.UnitPrice,
                    UsageCode = usage.Code,
                });

                await purchaseCreate.ExecuteAsync(invoice, userName);
                key = invoice.Key;
            }
            else
            {
                var invoice = new SalesInvoice
                {
                    Key = Guid.NewGuid(),
                    BranchCode = transaction.BranchCode,
                    CardCode = transaction.CardCode,
                    InvoiceType = SalesInvoiceType.Normal,
                    InvoiceDate = today,
                    CustodyStorageTransactionKey = transaction.Key,
                    GrossWeight = quantity,
                    NetWeight = quantity,
                    TruckCode = transaction.TruckCode,
                    Comments = comments,
                    Items =
                    [
                        new SalesInvoiceItem
                        {
                            ItemCode = transaction.ItemCode,
                            UnitOfMeasureCode = transaction.UoM,
                            Quantity = quantity,
                            UnitPrice = request.UnitPrice,
                            UsageCode = usage.Code,
                        },
                    ],
                };

                await salesCreate.ExecuteAsync(invoice, userName, CommitMode.Deferred);
                await db.SaveChangesAsync();
                key = invoice.Key;
            }

            await db.CommitAsync();
            return key;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao gerar a nota de custódia do romaneio {Key}", transaction.Key);
            throw;
        }
    }
}
