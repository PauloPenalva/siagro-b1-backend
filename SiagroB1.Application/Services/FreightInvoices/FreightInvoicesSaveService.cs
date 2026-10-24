using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>
/// Inclui e altera o rascunho da fatura de frete. Só STANDALONE. Cada CT-e é revalidado pela consulta de elegíveis;
/// a corrida entre duas faturas é barrada, em última instância, pelo índice único filtrado de FREIGHT_INVOICE_ITEMS.
/// </summary>
public class FreightInvoicesSaveService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService partners,
    FreightInvoiceEligibleCtesQuery eligible,
    IConfiguration configuration)
{
    public async Task<FreightInvoice> SaveAsync(FreightInvoiceSaveModel m, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);

        var keys = m.TransportDocumentKeys.Distinct().ToList();
        if (keys.Count == 0) throw new DefaultException("Selecione ao menos um CT-e.");

        var taker = await partners.GetByIdAsync(m.TakerCardCode)
            ?? throw new DefaultException("Tomador não encontrado.");

        FreightInvoice? invoice = null;
        if (m.Key is { } key)
        {
            invoice = await db.Context.FreightInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == key)
                ?? throw new DefaultException("Fatura de frete não encontrada.");
            if (invoice.Status != FreightInvoiceStatus.Draft)
                throw new DefaultException("Só o rascunho da fatura pode ser alterado.");
        }

        var rows = (await eligible.ListAsync(m.BranchCode, m.TakerCardCode, null, null, invoice?.Key))
            .ToDictionary(r => r.TransportDocumentKey);
        foreach (var k in keys.Where(k => !rows.ContainsKey(k)))
        {
            var number = await db.Context.TransportDocuments.Where(d => d.Key == k).Select(d => d.Number).FirstOrDefaultAsync();
            throw new DefaultException(
                $"CT-e {number} não pode ser faturado: não está autorizado, já está em outra fatura, não gera título ou possui baixa.");
        }
        var selected = keys.Select(k => rows[k]).ToList();

        var paymentCondition = m.PaymentConditionCode;
        if (paymentCondition is null)
            paymentCondition = await db.Context.TransportDocuments.Where(d => d.Key == keys[0])
                .Select(d => d.PaymentConditionCode).FirstOrDefaultAsync();

        var name = taker.CardName is { } n && n.Length > 100 ? n[..100] : taker.CardName;

        if (invoice is null)
        {
            var assignment = await AssignNumberAsync(m);
            invoice = new FreightInvoice
            {
                BranchCode = m.BranchCode, Code = assignment.Code, DocNumberKey = assignment.DocNumberKey,
                TakerCardCode = m.TakerCardCode, CreatedBy = user, Status = FreightInvoiceStatus.Draft,
            };
            db.Context.FreightInvoices.Add(invoice);
        }

        invoice.BranchCode = m.BranchCode;
        invoice.TakerCardCode = m.TakerCardCode;
        invoice.TakerName = name;
        invoice.IssueDate = m.IssueDate;
        invoice.DueDate = m.DueDate;
        invoice.PaymentConditionCode = paymentCondition;
        invoice.Observation = m.Observation;

        // Em rascunho o CT-e retirado tem a linha apagada; IsActive=false é reservado à fatura cancelada.
        foreach (var gone in invoice.Items.Where(i => !keys.Contains(i.TransportDocumentKey)).ToList())
        {
            invoice.Items.Remove(gone);
            db.Context.FreightInvoiceItems.Remove(gone);
        }
        var line = 1;
        foreach (var row in selected)
        {
            var item = invoice.Items.FirstOrDefault(i => i.TransportDocumentKey == row.TransportDocumentKey);
            if (item is null)
            {
                item = new FreightInvoiceItem { InvoiceKey = invoice.Key, TransportDocumentKey = row.TransportDocumentKey };
                invoice.Items.Add(item);
            }
            item.LineNum = line++;
            item.Amount = row.Amount;
            item.IsActive = true;
        }
        invoice.TotalAmount = selected.Sum(r => r.Amount);

        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<DocNumberAssignment> AssignNumberAsync(FreightInvoiceSaveModel m)
    {
        try
        {
            return await docNumberSequence.AssignAsync(new DocNumberRequest(
                TransactionCode.FreightInvoice, m.DocNumberKey, m.TypedCode, 20,
                c => db.Context.FreightInvoices.AnyAsync(x => x.Code == c)));
        }
        catch (NotFoundException) when (m.DocNumberKey is null)
        {
            throw new DefaultException(
                "Não há série de numeração padrão para a Fatura de Frete. Cadastre uma em Numeração de Documentos.");
        }
    }
}
