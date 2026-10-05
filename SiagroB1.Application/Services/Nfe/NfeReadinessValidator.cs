using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Confere o cadastro antes de reservar número (spec §9.2 passo 2). Junta TODAS as lacunas numa
/// mensagem só — emitente, configuração, certificado, chave do servidor, destinatário, entrega,
/// transportadora e condição de pagamento — e devolve o cadastro carregado para a montagem.
/// </summary>
public class NfeReadinessValidator(IUnitOfWork db, NfeOptions options)
{
    public async Task<NfeIssueContext> ValidateAsync(SalesInvoice invoice)
    {
        var problems = new List<string>();

        var branch = await db.Context.Branchs.AsNoTracking().Include(b => b.Municipality)
                         .FirstOrDefaultAsync(b => b.Code == invoice.BranchCode)
                     ?? throw new DefaultException($"Filial {invoice.BranchCode} não encontrada.");

        var branchGaps = Gaps(
            ("razão social", branch.LegalName), ("inscrição estadual", NfeText.AlphaNumeric(branch.StateRegistration)),
            ("logradouro", branch.Street), ("número", branch.StreetNumber), ("bairro", branch.District),
            ("município", branch.Municipality?.Code));
        if (NfeText.Digits(branch.ZipCode).Length != 8) branchGaps.Add("CEP");
        if (!IsTaxId(branch.TaxId)) branchGaps.Insert(0, "CNPJ");
        if (branch.TaxRegime is null) branchGaps.Add("regime tributário (CRT)");
        Report(problems, $"Filial {branch.Code}", branchGaps);

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branch.Code);
        if (settings is null)
            problems.Add("Configuração da NF-e da filial não cadastrada");
        else if (settings.CertificatePfx is null)
            problems.Add("Certificado digital não enviado (Configuração da NF-e)");
        else if (settings.CertificateValidUntil < DateTime.Now)
            problems.Add($"Certificado digital vencido em {settings.CertificateValidUntil:dd/MM/yyyy}");

        if (!options.HasCertificateKey)
            problems.Add("Chave Nfe:CertificateKey não configurada no servidor");

        var customer = await LoadPartnerAsync(invoice.CardCode);
        var customerAddress = customer is null ? null : BillingAddress(customer);
        if (customer is null)
        {
            problems.Add($"Cliente {invoice.CardCode} não encontrado");
        }
        else
        {
            var gaps = new List<string>();
            if (!IsTaxId(customer.TaxId)) gaps.Add("CNPJ/CPF");
            if (customer.StateRegistrationIndicator is null) gaps.Add("indicador da inscrição estadual");
            else if (customer.StateRegistrationIndicator == StateRegistrationIndicator.Taxpayer &&
                     string.IsNullOrWhiteSpace(NfeText.Digits(customer.StateRegistration))) gaps.Add("inscrição estadual");
            AddressGaps(gaps, customerAddress, "endereço de faturamento");
            Report(problems, $"Cliente {customer.CardCode}", gaps);
        }

        // CFOP x destino: 5xxx é dentro da UF da filial, 6xxx fora. O CFOP é gravado no cálculo do
        // item; mudar a UF do cliente depois sem recalcular o deixaria incoerente.
        var branchState = branch.Municipality?.StateAbbreviation ?? branch.StateCode;
        var destinationState = customerAddress?.Municipality?.StateAbbreviation ?? customerAddress?.State;
        if (!string.IsNullOrWhiteSpace(branchState) && !string.IsNullOrWhiteSpace(destinationState))
        {
            var sameState = string.Equals(branchState, destinationState, StringComparison.OrdinalIgnoreCase);
            var line = 0;

            foreach (var item in invoice.Items)
            {
                line++;
                var first = item.Cfop?.Trim().FirstOrDefault();

                // Venda: 5xxx dentro, 6xxx fora. Devolução própria (entrada): 1xxx dentro, 2xxx fora.
                var inStatePrefix = invoice.IsNfeReturn ? '1' : '5';
                var outStatePrefix = invoice.IsNfeReturn ? '2' : '6';

                if ((first == inStatePrefix && !sameState) || (first == outStatePrefix && sameState))
                    problems.Add($"Item {line}: CFOP {item.Cfop} não confere com o destino ({destinationState}) — salve o item de novo para recalcular.");
            }
        }

        BusinessPartner? deliveryPartner = null;
        Address? deliveryAddress = null;
        if (!string.IsNullOrWhiteSpace(invoice.DeliveryCardCode) && invoice.DeliveryCardCode != invoice.CardCode)
        {
            deliveryPartner = await LoadPartnerAsync(invoice.DeliveryCardCode);
            deliveryAddress = deliveryPartner is null ? null : DeliveryAddress(deliveryPartner);

            if (deliveryPartner is null)
            {
                problems.Add($"Local de entrega {invoice.DeliveryCardCode} não encontrado");
            }
            else
            {
                var gaps = new List<string>();
                if (!IsTaxId(deliveryPartner.TaxId)) gaps.Add("CNPJ/CPF");
                AddressGaps(gaps, deliveryAddress, "endereço");
                Report(problems, $"Local de entrega {deliveryPartner.CardCode}", gaps);
            }
        }

        BusinessPartner? carrier = null;
        if (!string.IsNullOrWhiteSpace(invoice.TruckingCompanyCode))
        {
            carrier = await LoadPartnerAsync(invoice.TruckingCompanyCode);
            if (carrier is null)
                problems.Add($"Transportadora {invoice.TruckingCompanyCode} não encontrada");
            else if (!string.IsNullOrWhiteSpace(carrier.TaxId) && !IsTaxId(carrier.TaxId))
                problems.Add($"Transportadora {carrier.CardCode}: CNPJ/CPF");
        }

        // Volume da NF-e: só os pesos são obrigatórios (quantidade, espécie, marca e numeração não).
        // Informados, não derivados da quantidade: a unidade pode ser saco, bag, caixa...
        if (invoice.GrossWeight <= 0 || invoice.NetWeight <= 0)
            problems.Add("Documento: peso bruto e peso líquido");
        else if (invoice.GrossWeight < invoice.NetWeight)
            problems.Add("Documento: o peso bruto não pode ser menor que o peso líquido");

        // A devolução própria não tem pagamento (tPag 90, rejeição 871 com qualquer outro meio).
        PaymentCondition? condition = null;
        if (!invoice.IsNfeReturn)
        {
            if (invoice.PaymentConditionCode is null)
            {
                problems.Add("Documento: condição de pagamento");
            }
            else
            {
                condition = await db.Context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == invoice.PaymentConditionCode);
                if (condition is null)
                    problems.Add($"Documento: condição de pagamento {invoice.PaymentConditionCode} não encontrada");
                else if (condition.Inactive)
                    problems.Add($"Documento: a condição de pagamento {condition.Name} está inativa");
            }
        }

        var returnOrigin = invoice.IsNfeReturn ? await LoadReturnOriginAsync(invoice, problems) : null;

        if (problems.Count > 0)
            throw new DefaultException("Faltam dados para emitir a NF-e:\n- " + string.Join("\n- ", problems));

        var (plate, truckState) = await LoadTruckAsync(invoice.TruckCode);
        var usageCodes = invoice.Items.Where(i => i.UsageCode is not null).Select(i => i.UsageCode!.Value).Distinct().ToList();
        var usages = await db.Context.Usages.AsNoTracking().Where(u => usageCodes.Contains(u.Code)).ToDictionaryAsync(u => u.Code);
        var itemCodes = invoice.Items.Select(i => i.ItemCode).Distinct().ToList();
        var itemCests = await db.Context.Items.AsNoTracking()
            .Where(i => itemCodes.Contains(i.ItemCode))
            .ToDictionaryAsync(i => i.ItemCode, i => i.Cest);

        return new NfeIssueContext(
            branch, branch.Municipality!, settings!, customer!, customerAddress!, customerAddress!.Municipality!,
            deliveryPartner, deliveryAddress, deliveryAddress?.Municipality,
            carrier, carrier is null ? null : BillingAddress(carrier),
            plate, truckState, condition, usages, itemCests, returnOrigin);
    }

    /// <summary>A venda da devolução própria: confirmada, com NF-e autorizada e o nItem de cada item devolvido.</summary>
    private async Task<NfeReturnOrigin?> LoadReturnOriginAsync(SalesInvoice invoice, List<string> problems)
    {
        var origin = await db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Key == invoice.SalesInvoiceOriginKey);

        if (origin is null)
        {
            problems.Add("Documento: venda de origem não encontrada");
            return null;
        }

        if (origin.NfeStatus != NfeStatus.Authorized || origin.ChaveNFe is not { Length: 44 })
        {
            problems.Add($"Venda de origem {origin.InvoiceNumber}: NF-e não autorizada");
            return null;
        }

        // Confirmada (ou já Devolvida por outras devoluções): uma venda estornada depois de a devolução
        // nascer faria a confirmação pós-autorização falhar com a NF-e já emitida.
        if (origin.InvoiceStatus is not (InvoiceStatus.Confirmed or InvoiceStatus.Returned))
        {
            problems.Add($"Venda de origem {origin.InvoiceNumber}: não está confirmada");
            return null;
        }

        var numbers = new Dictionary<Guid, int>();
        foreach (var item in invoice.Items)
        {
            var sold = origin.Items.FirstOrDefault(o => o.Key == item.SalesInvoiceItemOriginKey);
            var number = sold is null ? null : NfeItemNumbering.OriginNumber(sold, origin.Items.Count);

            if (number is null)
                problems.Add($"Item {item.ItemCode}: sem o item correspondente da NF-e de venda");
            else
                numbers[sold!.Key!.Value] = number.Value;
        }

        return new NfeReturnOrigin(origin.ChaveNFe, origin.TaxDocumentNumber!, origin.TaxDocumentSeries!, origin.InvoiceDate, numbers);
    }

    private Task<BusinessPartner?> LoadPartnerAsync(string cardCode) =>
        db.Context.BusinessPartners.AsNoTracking()
            .Include(p => p.Addresses).ThenInclude(a => a.Municipality)
            .FirstOrDefaultAsync(p => p.CardCode == cardCode);

    /// <summary>Mesma escolha de <c>SalesInvoicesCfopResolveService.ResolvePartnerState</c>: faturamento primeiro.</summary>
    private static Address? BillingAddress(BusinessPartner partner) =>
        partner.Addresses.FirstOrDefault(a =>
            string.Equals(a.AdresType, "B", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.AddressName, "FATURAMENTO", StringComparison.OrdinalIgnoreCase))
        ?? partner.Addresses.FirstOrDefault();

    /// <summary>Local de entrega: endereço de entrega com município; senão o de faturamento.</summary>
    private static Address? DeliveryAddress(BusinessPartner partner) =>
        partner.Addresses.FirstOrDefault(a =>
            string.Equals(a.AdresType, "S", StringComparison.OrdinalIgnoreCase) && a.MunicipalityCode is not null)
        ?? BillingAddress(partner);

    private async Task<(string? Plate, string? State)> LoadTruckAsync(string? truckCode)
    {
        if (string.IsNullOrWhiteSpace(truckCode))
            return (null, null);

        var truck = await db.Context.Trucks.AsNoTracking().Include(t => t.State).FirstOrDefaultAsync(t => t.Code == truckCode);

        return (NfeText.AlphaNumeric(truckCode), truck?.State?.Abbreviation);
    }

    private static bool IsTaxId(string? value) => NfeText.AlphaNumeric(value).Length is 11 or 14;

    private static void AddressGaps(List<string> gaps, Address? address, string label)
    {
        if (address is null)
        {
            gaps.Add(label);
            return;
        }

        gaps.AddRange(Gaps(
            ($"logradouro do {label}", address.Street), ($"número do {label}", address.StreetNumber),
            ($"bairro do {label}", address.Block), ($"município do {label}", address.Municipality?.Code)));
    }

    private static List<string> Gaps(params (string Label, string? Value)[] fields) =>
        fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Label).ToList();

    private static void Report(List<string> problems, string owner, List<string> gaps)
    {
        if (gaps.Count > 0)
            problems.Add($"{owner}: {string.Join(", ", gaps)}");
    }
}
