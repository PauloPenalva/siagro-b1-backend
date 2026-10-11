using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Lê o XML da NF-e de entrada e monta o rascunho do cabeçalho e das linhas, para o operador não
/// redigitar. SÓ LÊ — quem grava é o POST do documento.
///
/// NÃO tenta adivinhar a amarração linha → NF de origem. O layout da NF-e guarda as referências em
/// <c>ide/NFref</c>, que é do CABEÇALHO: o XML não diz qual linha veio de qual origem. Os emitentes
/// escrevem isso em texto livre no <c>infAdic/infCpl</c>, que é preservado e exibido na tela — a
/// amarração continua manual. Casar por quantidade erraria em silêncio, que aqui é o pior tipo de
/// erro.
///
/// Lido com <see cref="XDocument"/> e não com a Zeus.Net.NFe: a biblioteca está referenciada no
/// Infra mas nunca foi exercitada neste projeto, e o valor dela é na EMISSÃO. Para ler uma dezena
/// de campos de um layout estável, a dependência não paga o risco.
///
/// O rascunho só leva o <c>nItem</c> de cada item; a tributação é calculada pela natureza quando o documento é
/// salvo, e o <c>PurchaseInvoiceSupplierItemNumbers</c> confere o <c>nItem</c> contra o XML guardado.
/// </summary>
public class PurchaseInvoicesImportXmlService(IBusinessPartnerService businessPartnerService, IUnitOfWork db)
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public async Task<PurchaseInvoiceDraftDto> ExecuteAsync(byte[] xmlData, string fileName)
    {
        var nfe = SupplierNfeXmlReader.Read(xmlData);
        var infNfe = nfe.InfNfe;
        var ide = infNfe.Element(Nfe + "ide");
        var emit = infNfe.Element(Nfe + "emit");
        var cnpj = Value(emit, "CNPJ") ?? Value(emit, "CPF");

        var draft = new PurchaseInvoiceDraftDto
        {
            ChaveNFe = nfe.AccessKey,
            TaxDocumentNumber = Value(ide, "nNF"),
            TaxDocumentSeries = Value(ide, "serie"),
            IssueDate = ParseDate(Value(ide, "dhEmi") ?? Value(ide, "dEmi")),
            TotalDocumentValue = ParseDecimal(
                infNfe.Descendants(Nfe + "ICMSTot").FirstOrDefault(), "vNF"),
            TaxPayerComments = Value(infNfe.Element(Nfe + "infAdic"), "infCpl"),
            CardName = Value(emit, "xNome"),
            CardCode = ResolveCardCode(cnpj),
            XmlFileName = fileName,
        };

        foreach (var item in nfe.Items)
        {
            draft.Items.Add(new PurchaseInvoiceDraftItemDto
            {
                ItemCode = item.ProductCode,
                ItemName = item.ProductName,
                UnitOfMeasureCode = item.UnitOfMeasure,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                NfeItemNumber = item.ItemNumber,
                FreightValue = item.FreightValue,
                InsuranceValue = item.InsuranceValue,
                DiscountValue = item.DiscountValue,
                OtherExpensesValue = item.OtherExpensesValue,
            });
        }

        if (draft.Items.Count == 0)
            throw new DefaultException("XML sem itens (det).");

        await LinkStorageRemittancesAsync(draft, nfe.ReferencedKeys);

        return draft;
    }

    private async Task LinkStorageRemittancesAsync(PurchaseInvoiceDraftDto draft, IReadOnlyList<string> referencedKeys)
    {
        if (referencedKeys.Count == 0)
            return;

        var remittances = await StorageDepositRules.ActiveRemittances(db.Context).AsNoTracking()
            .Where(i => i.ChaveNFe != null && referencedKeys.Contains(i.ChaveNFe) && i.CardCode == draft.CardCode)
            .SelectMany(i => i.Items.Select(l => new
            {
                Key = l.Key!.Value, l.ItemCode, l.ItemName, l.UnitOfMeasureCode,
                Number = i.TaxDocumentNumber ?? i.InvoiceNumber, InvoiceKey = i.Key,
            }))
            .ToListAsync();

        if (remittances.Count == 0)
            return;

        // O armazém usa o próprio código de produto: com uma remessa e um item, a ligação é inequívoca e o rascunho
        // passa a usar o NOSSO produto.
        if (remittances.Select(r => r.InvoiceKey).Distinct().Count() == 1 && remittances.Count == 1 && draft.Items.Count == 1)
        {
            var only = remittances[0];
            var item = draft.Items[0];
            item.SalesInvoiceItemKey = only.Key;
            item.RemittanceNumber = only.Number;
            item.ItemCode = only.ItemCode;
            item.ItemName = only.ItemName;
            item.UnitOfMeasureCode = only.UnitOfMeasureCode;
            return;
        }

        foreach (var item in draft.Items)
        {
            var match = remittances.FirstOrDefault(r => r.ItemCode == item.ItemCode);
            if (match == null) continue;
            item.SalesInvoiceItemKey = match.Key;
            item.RemittanceNumber = match.Number;
        }
    }

    /// <summary>
    /// Resolve o emitente pelo CNPJ/CPF. Não encontrar é erro de negócio EXPLÍCITO: sem parceiro
    /// não há como listar as notas de origem, e deixar em branco só adiaria a descoberta para a
    /// hora de amarrar.
    /// </summary>
    private string ResolveCardCode(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            throw new DefaultException("XML sem CNPJ/CPF do emitente.");

        var digits = Digits(cnpj);

        // O EF (SQL Server) não traduz o Digits: traz só CardCode/TaxId e compara os dígitos em memória.
        var partner = businessPartnerService.QueryAll()
            .Where(p => p.TaxId != null)
            .Select(p => new { p.CardCode, p.TaxId })
            .AsEnumerable()
            .FirstOrDefault(p => Digits(p.TaxId!) == digits);

        return partner?.CardCode
               ?? throw new DefaultException(
                   $"Nenhum parceiro cadastrado com o CNPJ/CPF {cnpj} do emitente do XML.");
    }

    /// <summary>Compara só os dígitos: o cadastro guarda com máscara e o XML sem.</summary>
    private static string Digits(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string? Value(XElement? parent, string name) =>
        parent?.Element(Nfe + name)?.Value;

    private static decimal ParseDecimal(XElement? parent, string name) =>
        decimal.TryParse(Value(parent, name), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
}
