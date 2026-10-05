using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// O que a prontidão precisa do documento, sem saber se é de saída ou de entrada.
/// </summary>
/// <param name="PartnerLabel">Como o destinatário aparece nas lacunas ("Cliente", "Fornecedor").</param>
/// <param name="RequiresPayment">Falso na devolução própria, que não tem pagamento (tPag 90).</param>
/// <param name="Direction">Sentido da NF-e: decide os prefixos de CFOP conferidos (5/6 na saída, 1/2 na entrada).</param>
/// <param name="RequiresProductAndUnit">Linha cujo produto/unidade é anulável: lista a lacuna em vez de estourar no montador.</param>
public sealed record NfeReadinessRequest(
    string? BranchCode, string PartnerCode, string PartnerLabel, string? DeliveryCardCode, string? TruckingCompanyCode,
    string? TruckCode, decimal GrossWeight, decimal NetWeight, int? PaymentConditionCode, bool RequiresPayment,
    NfeDirection Direction, IReadOnlyList<INfeTaxedLine> Lines, bool RequiresProductAndUnit);

/// <summary>
/// Confere o cadastro antes de reservar número (spec §9.2 passo 2). Junta TODAS as lacunas numa
/// mensagem só — emitente, configuração, certificado, chave do servidor, destinatário, entrega,
/// transportadora e condição de pagamento — e devolve o cadastro carregado para a montagem.
/// </summary>
public class NfeReadinessValidator(IUnitOfWork db, NfeOptions options)
{
    public Task<NfeIssueContext> ValidateAsync(SalesInvoice invoice) =>
        ValidateCoreAsync(
            new NfeReadinessRequest(
                invoice.BranchCode, invoice.CardCode, "Cliente", invoice.DeliveryCardCode, invoice.TruckingCompanyCode,
                invoice.TruckCode, invoice.GrossWeight, invoice.NetWeight, invoice.PaymentConditionCode,
                RequiresPayment: !invoice.IsNfeReturn,
                // Venda: saída. Devolução própria: entrada.
                Direction: invoice.IsNfeReturn ? NfeDirection.Incoming : NfeDirection.Outgoing,
                Lines: invoice.Items.Cast<INfeTaxedLine>().ToList(),
                RequiresProductAndUnit: false),
            invoice.IsNfeReturn ? problems => LoadReturnOriginAsync(invoice, problems) : null);

    /// <summary>
    /// O núcleo, sem saber de que documento se trata: o que muda vem no <paramref name="request"/>, e a
    /// operação de origem (só na devolução) em <paramref name="loadReturnOrigin"/>, que acrescenta as
    /// próprias lacunas à lista.
    /// </summary>
    private async Task<NfeIssueContext> ValidateCoreAsync(
        NfeReadinessRequest request, Func<List<string>, Task<NfeReturnOrigin?>>? loadReturnOrigin)
    {
        var problems = new List<string>();

        var branch = await db.Context.Branchs.AsNoTracking().Include(b => b.Municipality)
                         .FirstOrDefaultAsync(b => b.Code == request.BranchCode)
                     ?? throw new DefaultException($"Filial {request.BranchCode} não encontrada.");

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

        var customer = await LoadPartnerAsync(request.PartnerCode);
        var customerAddress = customer is null ? null : BillingAddress(customer);
        if (customer is null)
        {
            problems.Add($"{request.PartnerLabel} {request.PartnerCode} não encontrado");
        }
        else
        {
            var gaps = new List<string>();
            if (!IsTaxId(customer.TaxId)) gaps.Add("CNPJ/CPF");
            if (customer.StateRegistrationIndicator is null) gaps.Add("indicador da inscrição estadual");
            else if (customer.StateRegistrationIndicator == StateRegistrationIndicator.Taxpayer &&
                     string.IsNullOrWhiteSpace(NfeText.Digits(customer.StateRegistration))) gaps.Add("inscrição estadual");
            AddressGaps(gaps, customerAddress, "endereço de faturamento");
            Report(problems, $"{request.PartnerLabel} {customer.CardCode}", gaps);
        }

        // Linha de documento em que produto e unidade são anuláveis: o montador usa os dois sem conferir.
        if (request.RequiresProductAndUnit)
        {
            var position = 0;
            foreach (var line in request.Lines)
            {
                position++;
                var gaps = new List<string>();
                if (string.IsNullOrWhiteSpace(line.ItemCode)) gaps.Add("produto");
                if (string.IsNullOrWhiteSpace(line.UnitOfMeasureCode)) gaps.Add("unidade de medida");
                Report(problems, $"Item {position}", gaps);
            }
        }

        // CFOP x destino: 5xxx é dentro da UF da filial, 6xxx fora. O CFOP é gravado no cálculo do
        // item; mudar a UF do cliente depois sem recalcular o deixaria incoerente.
        var branchState = branch.Municipality?.StateAbbreviation ?? branch.StateCode;
        var destinationState = customerAddress?.Municipality?.StateAbbreviation ?? customerAddress?.State;
        if (!string.IsNullOrWhiteSpace(branchState) && !string.IsNullOrWhiteSpace(destinationState))
        {
            var sameState = string.Equals(branchState, destinationState, StringComparison.OrdinalIgnoreCase);
            var line = 0;

            foreach (var item in request.Lines)
            {
                line++;
                var first = item.Cfop?.Trim().FirstOrDefault();

                // Saída: 5xxx dentro, 6xxx fora. Entrada (ex.: devolução própria de venda): 1xxx dentro, 2xxx fora.
                var inStatePrefix = request.Direction == NfeDirection.Incoming ? '1' : '5';
                var outStatePrefix = request.Direction == NfeDirection.Incoming ? '2' : '6';

                if ((first == inStatePrefix && !sameState) || (first == outStatePrefix && sameState))
                    problems.Add($"Item {line}: CFOP {item.Cfop} não confere com o destino ({destinationState}) — salve o item de novo para recalcular.");
            }
        }

        BusinessPartner? deliveryPartner = null;
        Address? deliveryAddress = null;
        if (!string.IsNullOrWhiteSpace(request.DeliveryCardCode) && request.DeliveryCardCode != request.PartnerCode)
        {
            deliveryPartner = await LoadPartnerAsync(request.DeliveryCardCode);
            deliveryAddress = deliveryPartner is null ? null : DeliveryAddress(deliveryPartner);

            if (deliveryPartner is null)
            {
                problems.Add($"Local de entrega {request.DeliveryCardCode} não encontrado");
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
        if (!string.IsNullOrWhiteSpace(request.TruckingCompanyCode))
        {
            carrier = await LoadPartnerAsync(request.TruckingCompanyCode);
            if (carrier is null)
                problems.Add($"Transportadora {request.TruckingCompanyCode} não encontrada");
            else if (!string.IsNullOrWhiteSpace(carrier.TaxId) && !IsTaxId(carrier.TaxId))
                problems.Add($"Transportadora {carrier.CardCode}: CNPJ/CPF");
        }

        // Volume da NF-e: só os pesos são obrigatórios (quantidade, espécie, marca e numeração não).
        // Informados, não derivados da quantidade: a unidade pode ser saco, bag, caixa...
        if (request.GrossWeight <= 0 || request.NetWeight <= 0)
            problems.Add("Documento: peso bruto e peso líquido");
        else if (request.GrossWeight < request.NetWeight)
            problems.Add("Documento: o peso bruto não pode ser menor que o peso líquido");

        // A devolução própria não tem pagamento (tPag 90, rejeição 871 com qualquer outro meio).
        PaymentCondition? condition = null;
        if (request.RequiresPayment)
        {
            if (request.PaymentConditionCode is null)
            {
                problems.Add("Documento: condição de pagamento");
            }
            else
            {
                condition = await db.Context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == request.PaymentConditionCode);
                if (condition is null)
                    problems.Add($"Documento: condição de pagamento {request.PaymentConditionCode} não encontrada");
                else if (condition.Inactive)
                    problems.Add($"Documento: a condição de pagamento {condition.Name} está inativa");
            }
        }

        var returnOrigin = loadReturnOrigin is null ? null : await loadReturnOrigin(problems);

        if (problems.Count > 0)
            throw new DefaultException("Faltam dados para emitir a NF-e:\n- " + string.Join("\n- ", problems));

        var (plate, truckState) = await LoadTruckAsync(request.TruckCode);
        var usageCodes = request.Lines.Where(i => i.UsageCode is not null).Select(i => i.UsageCode!.Value).Distinct().ToList();
        var usages = await db.Context.Usages.AsNoTracking().Where(u => usageCodes.Contains(u.Code)).ToDictionaryAsync(u => u.Code);
        var itemCodes = request.Lines.Select(i => i.ItemCode).Where(c => c is not null).Select(c => c!).Distinct().ToList();
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
