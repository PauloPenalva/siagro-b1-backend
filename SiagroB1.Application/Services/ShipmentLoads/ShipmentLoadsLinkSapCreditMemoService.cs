using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Vincular NF de devolução" (GAC-1226): grava, no documento de devolução JÁ confirmado da carga, os dados da NF
/// de devolução emitida no SAP (A/R Credit Memo, ORIN). Tudo é relido e revalidado aqui — o que a tela mostrou
/// pode ter mudado no SAP.
/// </summary>
/// <remarks>
/// O vínculo é só fiscal: a recusa da carga já moveu saldo, armazém e contrato quando foi registrada. Este serviço
/// NÃO pode chamar confirmação, recálculo nem hook de carga — só escreve os campos da nota no documento e nas
/// linhas dele.
/// </remarks>
public class ShipmentLoadsLinkSapCreditMemoService(
    IUnitOfWork db,
    SapLinkableCreditMemosService finder,
    SalesInvoiceTaxDocumentDuplicateGuard duplicateGuard,
    SalesInvoicesChangeLogService changeLog,
    IConfiguration? configuration = null,
    ILogger<ShipmentLoadsLinkSapCreditMemoService>? logger = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string SapOnlyMessage = "Disponível somente com integração SAP Business One.";

    public const string AlreadyLinkedMessage = "Esta devolução já está vinculada a uma NF de devolução do SAP.";

    public const string UnavailableMessage = "A NF de devolução do SAP não está mais disponível para vínculo.";

    public const string LinkedElsewhereMessage = "A NF de devolução do SAP já foi vinculada a outro documento.";

    private const string PersistenceFailure =
        "Não foi possível vincular a NF de devolução do SAP. Nenhuma alteração foi gravada. Tente novamente.";

    public async Task<SalesInvoice> ExecuteAsync(Guid loadKey, Guid returnInvoiceKey, int sapDocEntry, string username)
    {
        if (!ErpMode.IsSapB1(configuration))
            throw new ApplicationException(SapOnlyMessage);

        // Antes de ir ao SAP: a devolução que já tem nota recebe esta mensagem, seja qual for a nota pedida.
        var alreadyLinked = await db.Context.SalesInvoices.AsNoTracking()
            .AnyAsync(x => x.Key == returnInvoiceKey && x.ShipmentLoadKey == loadKey && x.SapCreditMemoDocEntry != null);
        if (alreadyLinked)
            throw new ApplicationException(AlreadyLinkedMessage);

        // Mesmas regras da listagem; também recusa o documento que não é devolução confirmada desta carga.
        var creditMemo = await finder.FindAsync(loadKey, returnInvoiceKey, sapDocEntry)
                         ?? throw new ApplicationException(UnavailableMessage);

        var invoice = await db.Context.SalesInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == returnInvoiceKey)
            ?? throw new NotFoundException($"Sales invoice not found key {returnInvoiceKey}");

        if (invoice.SapCreditMemoDocEntry is not null)
            throw new ApplicationException(AlreadyLinkedMessage);

        if (!creditMemo.QuantityMatches)
            throw new ApplicationException(
                $"A quantidade da NF de devolução ({creditMemo.Quantity.ToString("N3", PtBr)}) é diferente da " +
                $"quantidade da devolução ({invoice.Items.Sum(i => i.Quantity).ToString("N3", PtBr)}).");

        await db.BeginTransactionAsync();
        try
        {
            await duplicateGuard.EnsureUniqueAsync(
                invoice.BranchCode ?? string.Empty, creditMemo.TaxDocumentNumber, creditMemo.TaxDocumentSeries,
                creditMemo.AccessKey, invoice.Key);

            invoice.SapCreditMemoDocEntry = creditMemo.DocEntry;
            invoice.SapCreditMemoDocNum = creditMemo.DocNum;
            // Havia "Sem NF" ou número digitado antes: a nota do SAP passa a valer.
            invoice.WithoutTaxDocument = false;
            ShipmentBillingCreateSalesInvoiceService.ApplySapFiscalFields(
                invoice, creditMemo.TaxDocumentNumber!, creditMemo.TaxDocumentSeries!, creditMemo.AccessKey,
                creditMemo.Cfop, creditMemo.UsageCode, creditMemo.UsageName);

            changeLog.Register(
                invoice.Key, ContractChangeLogFields.SapReturnInvoice, null,
                Describe(creditMemo.TaxDocumentNumber, creditMemo.TaxDocumentSeries, creditMemo.DocNum), username);

            await SaveAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();

            if (HasDbUpdateException(e))
            {
                // Dois usuários vinculando a mesma nota: o índice único filtrado derruba o perdedor. A causa é
                // confirmada relendo o banco, nunca pela mensagem do EF.
                var takenByAnother = await db.Context.SalesInvoices.AsNoTracking().AnyAsync(x =>
                    x.SapCreditMemoDocEntry == sapDocEntry && x.Key != returnInvoiceKey
                    && x.InvoiceStatus != InvoiceStatus.Cancelled && x.InvoiceStatus != InvoiceStatus.Returned);

                if (takenByAnother)
                    throw new ApplicationException(LinkedElsewhereMessage, e);

                logger?.LogError(e, "Falha ao gravar o vínculo da NF de devolução do SAP no documento {Key}", returnInvoiceKey);
                throw new ApplicationException(PersistenceFailure, e);
            }

            throw;
        }

        return invoice;
    }

    /// <summary>Gravação. Costura de teste: permite simular a corrida que o índice único resolve.</summary>
    protected virtual Task SaveAsync() => db.SaveChangesAsync();

    /// <summary>Como a nota aparece no log de alterações do documento.</summary>
    internal static string Describe(string? number, string? series, int? docNum) =>
        $"NF {number}/{series} (doc. SAP {docNum})";

    private static bool HasDbUpdateException(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
            if (inner is DbUpdateException)
                return true;
        return false;
    }
}
