using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Aplica no documento o retorno da SEFAZ (spec §9.2 passo 6 e §9.3) — o mesmo para emissão e
/// consulta. Autorizada: grava o procNFe e a situação, SALVA, e só então confirma o documento.
/// </summary>
public class SalesInvoiceNfeResultHandler(
    IUnitOfWork db, SalesInvoicesConfirmService confirm, ILogger<SalesInvoiceNfeResultHandler> logger)
{
    public Task<NfeIssueOutcomeDto> ApplyAuthorizationAsync(
        SalesInvoice invoice, string signedXml, NfeSefazResult result, string userName) =>
        ApplyAsync(invoice, [signedXml], result, userName, fromConsult: false);

    /// <param name="signedXmls">XMLs assinados candidatos, o mais novo primeiro.</param>
    public Task<NfeIssueOutcomeDto> ApplyConsultAsync(
        SalesInvoice invoice, IReadOnlyList<string> signedXmls, NfeSefazResult result, string userName) =>
        ApplyAsync(invoice, signedXmls, result, userName, fromConsult: true);

    /// <summary>
    /// Confirma o documento da NF-e autorizada. Falhou: a NF-e (já gravada) é a verdade; o erro
    /// vai para <c>NfeConfirmationError</c> e "Concluir confirmação" tenta de novo.
    /// </summary>
    public async Task<NfeIssueOutcomeDto> ConfirmAsync(Guid key, string userName)
    {
        try
        {
            await confirm.ExecuteAsync(key, userName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao confirmar o documento {InvoiceKey} depois da NF-e autorizada.", key);

            // ⚠️ A confirmação mexe em entidades rastreadas antes de falhar, e o rollback da
            // transação dela não desfaz o rastreador: salvar por cima gravaria a confirmação pela
            // metade. Descarta tudo e grava só o erro, relendo o documento.
            db.Context.ChangeTracker.Clear();

            var failed = await db.Context.SalesInvoices.FirstAsync(i => i.Key == key);
            failed.NfeConfirmationError = Truncate(e.Message);
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(failed);
        }

        var confirmed = await db.Context.SalesInvoices.FirstAsync(i => i.Key == key);
        if (confirmed.NfeConfirmationError is not null)
        {
            confirmed.NfeConfirmationError = null;
            await db.SaveChangesAsync();
        }

        return NfeIssueOutcomeDto.From(confirmed);
    }

    private async Task<NfeIssueOutcomeDto> ApplyAsync(
        SalesInvoice invoice, IReadOnlyList<string> signedXmls, NfeSefazResult result, string userName, bool fromConsult)
    {
        invoice.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        invoice.NfeStatusReason = Truncate(result.Reason);

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

            db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
            {
                Key = Guid.NewGuid(),
                SalesInvoiceKey = invoice.Key,
                Kind = NfeXmlKind.Authorized,
                Xml = NfeProcComposer.Compose(signedXml, result.ProtocolXml),
                CreatedAt = DateTime.Now,
            });

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
            invoice.NfeStatusReason = Truncate($"{result.Reason} — use Consultar situação.");
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
                    db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
                    {
                        Key = Guid.NewGuid(),
                        SalesInvoiceKey = invoice.Key,
                        Kind = NfeXmlKind.Denied,
                        Xml = NfeProcComposer.Compose(deniedSigned, result.ProtocolXml),
                        CreatedAt = DateTime.Now,
                    });
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

    internal static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
