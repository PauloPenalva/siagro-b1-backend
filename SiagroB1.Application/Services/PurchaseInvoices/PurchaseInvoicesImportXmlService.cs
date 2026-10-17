using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Lê o XML da NF-e de entrada e monta o rascunho do cabeçalho e das linhas, para o operador não
/// redigitar. SÓ LÊ — quem grava é o POST do documento. Única exceção à regra abaixo: o retorno de depósito, em
/// que o NFref aponta a remessa (ver <c>LinkStorageRemittancesAsync</c>).
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
        if (SupplierCteXmlReader.IsCte(xmlData))
            return await ExecuteCteAsync(xmlData, fileName);

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
            InvoiceType = nfe.IsComplement ? PurchaseInvoiceType.ValueComplement : PurchaseInvoiceType.Normal,
        };

        foreach (var item in nfe.Items)
        {
            draft.Items.Add(new PurchaseInvoiceDraftItemDto
            {
                ItemCode = item.ProductCode,
                ItemName = item.ProductName,
                UnitOfMeasureCode = item.UnitOfMeasure,
                // NF-e de complemento (D11): só o valor da linha conta; quantidade e preço unitário ficam zerados (P11).
                Quantity = nfe.IsComplement ? 0m : item.Quantity,
                UnitPrice = nfe.IsComplement ? 0m : item.UnitPrice,
                ComplementAmount = nfe.IsComplement ? item.ProductValue : 0m,
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

    /// <summary>
    /// CT-e de frete tomado: um rascunho de UMA linha de serviço. O vínculo com as NF-e transportadas NÃO vai aqui —
    /// nasce na gravação, a partir do XML guardado.
    /// </summary>
    private async Task<PurchaseInvoiceDraftDto> ExecuteCteAsync(byte[] xmlData, string fileName)
    {
        var cte = SupplierCteXmlReader.Read(xmlData);

        // O modelo está na chave (posições 20-21). O CT-e OS (67) usa o mesmo infCte, mas não é o frete tomado desta tela.
        var model = cte.AccessKey.Length >= 22 ? cte.AccessKey.Substring(20, 2) : string.Empty;
        if (model != "57")
            throw new DefaultException($"Só CT-e modelo 57 pode ser escriturado (o arquivo é modelo {model}).");

        if (cte.CteType != "0")
            throw new DefaultException($"Só CT-e normal pode ser escriturado nesta versão (tpCTe {cte.CteType}).");

        var cardCode = ResolveCardCode(cte.IssuerTaxId);

        // A UF/dígitos não são traduzíveis pelo EF: traz as filiais e compara os dígitos em memória.
        var payerDigits = Digits(cte.PayerTaxId ?? string.Empty);
        var branch = payerDigits.Length == 0
            ? null
            : (await db.Context.Branchs.AsNoTracking().ToListAsync())
                .FirstOrDefault(b => b.TaxId != null && Digits(b.TaxId) == payerDigits);

        if (branch == null)
            throw new DefaultException($"O tomador do CT-e (CNPJ {cte.PayerTaxId}) não é uma filial cadastrada.");

        var br = CultureInfo.GetCultureInfo("pt-BR");
        var draft = new PurchaseInvoiceDraftDto
        {
            TaxDocumentKind = TaxDocumentKind.Cte,
            InvoiceType = PurchaseInvoiceType.Normal,
            ChaveNFe = cte.AccessKey,
            TaxDocumentNumber = cte.Number,
            TaxDocumentSeries = cte.Series,
            IssueDate = cte.IssueDate,
            TotalDocumentValue = cte.ServiceTotal,
            TaxPayerComments = cte.Observations,
            CardName = cte.IssuerName,
            CardCode = cardCode,
            TruckingCompanyCode = cardCode,
            BranchCode = branch.Code,
            XmlFileName = fileName,
            FreightStartState = cte.StartState,
            FreightEndState = cte.EndState,
            FreightStartCity = cte.StartCity,
            FreightEndCity = cte.EndCity,
            FreightPayerTaxId = cte.PayerTaxId,
            CteIcmsCst = cte.IcmsCst,
            CteIcmsValue = cte.IcmsValue,
        };

        if (cte.AmountReceivable != cte.ServiceTotal)
            draft.Warnings.Add(
                $"O valor a receber do CT-e (R$ {cte.AmountReceivable.ToString("N2", br)}) difere do total da prestação " +
                $"(R$ {cte.ServiceTotal.ToString("N2", br)}); confira o título a pagar.");

        string? itemName = null;
        if (string.IsNullOrWhiteSpace(branch.FreightServiceItemCode))
            draft.Warnings.Add("Filial sem produto padrão de frete: informe o produto da linha.");
        else
            itemName = await db.Context.Items.AsNoTracking()
                .Where(i => i.ItemCode == branch.FreightServiceItemCode)
                .Select(i => i.ItemName).FirstOrDefaultAsync();

        // O Item não tem unidade padrão: UnitOfMeasureCode fica nulo e o operador informa na linha.
        draft.Items.Add(new PurchaseInvoiceDraftItemDto
        {
            ItemCode = string.IsNullOrWhiteSpace(branch.FreightServiceItemCode) ? null : branch.FreightServiceItemCode,
            ItemName = itemName ?? "SERVIÇO DE TRANSPORTE",
            Quantity = 1m,
            UnitPrice = cte.ServiceTotal,
        });

        return draft;
    }

    private async Task LinkStorageRemittancesAsync(PurchaseInvoiceDraftDto draft, IReadOnlyList<string> referencedKeys)
    {
        if (referencedKeys.Count == 0)
            return;

        var remittances = await StorageDepositRules
            .RemittanceLinesByAccessKeys(db.Context, referencedKeys.ToList(), draft.CardCode).ToListAsync();

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
