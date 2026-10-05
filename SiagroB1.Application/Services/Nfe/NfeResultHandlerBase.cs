using System.Globalization;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Aplica no documento o retorno da SEFAZ (spec §9.2 passo 6 e §9.3) — o mesmo para emissão e
/// consulta. Autorizada: grava o procNFe e a situação, SALVA, e só então confirma o documento.
/// </summary>
/// <remarks>
/// Uma cópia só para os dois documentos: a conferência pelo digest, a ordem "grava a autorizada, salva,
/// confirma" e o descarte do rastreador na confirmação que falha não podem divergir entre eles.
/// </remarks>
public abstract class NfeResultHandlerBase<TDocument>(IUnitOfWork db, INfeDocumentStore<TDocument> store, ILogger logger)
    where TDocument : class, INfeDocument
{
    public Task<NfeIssueOutcomeDto> ApplyAuthorizationAsync(
        TDocument document, string signedXml, NfeSefazResult result, string userName) =>
        ApplyAsync(document, [signedXml], result, userName, fromConsult: false);

    /// <param name="signedXmls">XMLs assinados candidatos, o mais novo primeiro.</param>
    public Task<NfeIssueOutcomeDto> ApplyConsultAsync(
        TDocument document, IReadOnlyList<string> signedXmls, NfeSefazResult result, string userName) =>
        ApplyAsync(document, signedXmls, result, userName, fromConsult: true);

    /// <summary>
    /// Confirma o documento da NF-e autorizada. Falhou: a NF-e (já gravada) é a verdade; o erro
    /// vai para <c>NfeConfirmationError</c> e "Concluir confirmação" tenta de novo.
    /// </summary>
    public async Task<NfeIssueOutcomeDto> ConfirmAsync(Guid key, string userName)
    {
        try
        {
            await ConfirmDocumentAsync(key, userName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao confirmar o documento {InvoiceKey} depois da NF-e autorizada.", key);

            // ⚠️ A confirmação mexe em entidades rastreadas antes de falhar, e o rollback da
            // transação dela não desfaz o rastreador: salvar por cima gravaria a confirmação pela
            // metade. Descarta tudo e grava só o erro, relendo o documento.
            db.Context.ChangeTracker.Clear();

            var failed = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);
            failed.NfeConfirmationError = NfeStatusText.Truncate(e.Message);
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(failed);
        }

        var confirmed = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);
        if (confirmed.NfeConfirmationError is not null)
        {
            confirmed.NfeConfirmationError = null;
            await db.SaveChangesAsync();
        }

        return NfeIssueOutcomeDto.From(confirmed);
    }

    /// <summary>A confirmação do documento (estoque, saldo, financeiro) — a de cada tipo de documento.</summary>
    protected abstract Task ConfirmDocumentAsync(Guid key, string userName);

    private async Task<NfeIssueOutcomeDto> ApplyAsync(
        TDocument invoice, IReadOnlyList<string> signedXmls, NfeSefazResult result, string userName, bool fromConsult)
    {
        invoice.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        invoice.NfeStatusReason = NfeStatusText.Truncate(result.Reason);

        if (NfeStatusCodes.IsAuthorized(result.StatusCode) && result.ProtocolXml is not null)
        {
            // O protocolo autoriza UM XML (pelo digest): se houve reenvio com dados diferentes, o
            // autorizado pode ser o de uma tentativa anterior. Sem correspondência, não autoriza.
            var protocolDigest = NfeProcComposer.ProtocolDigest(result.ProtocolXml);
            var signedXml = signedXmls.FirstOrDefault(x => NfeProcComposer.SignedDigest(x) == protocolDigest);

            if (signedXml is null)
            {
                invoice.NfeStatus = NfeStatus.Processing;
                invoice.NfeStatusReason =
                    "A SEFAZ tem esta NF-e autorizada, mas nenhum XML assinado gravado confere com ela. Procure o suporte.";
                await db.SaveChangesAsync();

                return NfeIssueOutcomeDto.From(invoice);
            }

            store.AddXml(invoice, NfeXmlKind.Authorized, NfeProcComposer.Compose(signedXml, result.ProtocolXml));

            invoice.NfeStatus = NfeStatus.Authorized;
            invoice.NfeProtocol = result.Protocol;
            invoice.NfeAuthorizedAt = (result.ReceivedAt ?? DateTimeOffset.Now).DateTime;
            invoice.NfeConfirmationError = null;

            // A NF-e autorizada vai para o banco ANTES da confirmação: se ela falhar, a nota
            // continua registrada como autorizada.
            await db.SaveChangesAsync();

            return await ConfirmAsync(invoice.Key, userName);
        }

        // 103/105 (lote recebido / em processamento) não é rejeição: a nota ainda será processada.
        if (!fromConsult && result.StatusCode is 103 or 105)
        {
            invoice.NfeStatus = NfeStatus.Processing;
            invoice.NfeStatusReason = NfeStatusText.Truncate($"{result.Reason} — use Consultar situação.");
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        if (NfeStatusCodes.IsDenied(result.StatusCode))
        {
            invoice.NfeStatus = NfeStatus.Denied;
            invoice.NfeProtocol = result.Protocol;

            // O emitente guarda o XML da NF-e denegada: mesmo procNFe da autorizada, com o XML
            // assinado que o protocolo aponta (pelo digest).
            if (result.ProtocolXml is not null)
            {
                var protocolDigest = NfeProcComposer.ProtocolDigest(result.ProtocolXml);
                var deniedSigned = signedXmls.FirstOrDefault(x => NfeProcComposer.SignedDigest(x) == protocolDigest);

                if (deniedSigned is not null)
                    store.AddXml(invoice, NfeXmlKind.Denied, NfeProcComposer.Compose(deniedSigned, result.ProtocolXml));
            }
        }
        else if (!fromConsult || result.StatusCode == NfeStatusCodes.NotFound)
        {
            // Rejeitada (ou "não consta na base" na consulta): pode corrigir e reenviar.
            invoice.NfeStatus = NfeStatus.Rejected;
        }

        // Consulta com outro retorno: segue em processamento, com o código e o motivo gravados.
        await db.SaveChangesAsync();

        return NfeIssueOutcomeDto.From(invoice);
    }
}
