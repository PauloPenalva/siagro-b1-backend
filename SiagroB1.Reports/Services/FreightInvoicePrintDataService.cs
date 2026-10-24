using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Interfaces;

namespace SiagroB1.Reports.Services;

/// <summary>Monta o impresso da fatura de frete: emitente (filial), tomador, parcelas do título, CT-e e conta de recebimento.</summary>
public class FreightInvoicePrintDataService(IUnitOfWork db) : IFreightInvoicePrintDataService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<FreightInvoicePrintDto> GetAsync(Guid freightInvoiceKey, CancellationToken ct = default)
    {
        var context = db.Context;
        var invoice = await context.FreightInvoices.AsNoTracking()
                          .Include(x => x.Items)
                          .FirstOrDefaultAsync(x => x.Key == freightInvoiceKey, ct)
                      ?? throw new NotFoundException("Fatura de frete não encontrada.");

        if (invoice.Status == FreightInvoiceStatus.Cancelled)
            throw new BusinessException("Fatura de frete cancelada.");

        var branch = await context.Branchs.AsNoTracking().Include(x => x.Municipality)
            .FirstOrDefaultAsync(x => x.Code == invoice.BranchCode, ct);

        var activeItems = invoice.Items.Where(i => i.IsActive).OrderBy(i => i.LineNum).ToList();
        var keys = activeItems.Select(i => i.TransportDocumentKey).ToList();
        var ctes = await context.TransportDocuments.AsNoTracking().Include(x => x.Quantities)
            .Where(x => keys.Contains(x.Key)).ToDictionaryAsync(x => x.Key, ct);

        var codes = ctes.Values.SelectMany(c => new[] { c.StartMunicipalityCode, c.EndMunicipalityCode })
            .Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct().ToList();
        var cities = await context.Municipalities.AsNoTracking().Where(m => codes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, m => m.Name, ct);

        var items = activeItems.Select(i =>
        {
            ctes.TryGetValue(i.TransportDocumentKey, out var cte);
            return new FreightInvoicePrintItemDto
            {
                LineNum = i.LineNum,
                Number = cte?.Number?.ToString(PtBr) ?? "",
                Series = cte?.Series.ToString(PtBr) ?? "",
                AccessKey = cte?.AccessKey ?? "",
                IssuedAt = cte?.IssuedAt?.ToString("dd/MM/yyyy", PtBr) ?? "",
                Route = cte is null ? "" : $"{Place(cte.StartMunicipalityCode, cte.StartState, cities)} → {Place(cte.EndMunicipalityCode, cte.EndState, cities)}",
                WeightKg = cte is null ? 0m : TransportDocumentWeight.GrossKg(cte),
                Amount = i.Amount
            };
        }).ToList();

        var installments = (await context.FinancialDocuments.AsNoTracking()
                .Where(d => d.OriginType == FinancialDocumentOrigin.FreightInvoice && d.OriginKey == invoice.Key
                            && d.Status != FinancialDocumentStatus.Canceled)
                .Select(d => new { d.InstallmentNumber, d.DueDate, d.NetAmount })
                .ToListAsync(ct))
            .OrderBy(d => d.DueDate).ThenBy(d => d.InstallmentNumber)
            .Select((d, idx) => new FreightInvoicePrintInstallmentDto
            {
                Installment = (d.InstallmentNumber ?? idx + 1).ToString(PtBr),
                DueDate = d.DueDate.ToString("dd/MM/yyyy", PtBr),
                Amount = d.NetAmount
            }).ToList();

        // Fatura ainda sem título (rascunho): mostra o vencimento previsto, se houver.
        if (installments.Count == 0 && invoice.DueDate is { } due)
            installments.Add(new FreightInvoicePrintInstallmentDto { Installment = "1", DueDate = due.ToString("dd/MM/yyyy", PtBr), Amount = invoice.TotalAmount });

        var partner = await context.BusinessPartners.AsNoTracking().Include(p => p.Addresses).ThenInclude(a => a.Municipality)
            .FirstOrDefaultAsync(p => p.CardCode == invoice.TakerCardCode, ct);
        var takerAddress = partner?.Addresses.FirstOrDefault(a => a.AdresType == "B") ?? partner?.Addresses.FirstOrDefault();

        var paymentCondition = "";
        if (invoice.PaymentConditionCode is { } pc)
            paymentCondition = await context.PaymentConditions.AsNoTracking().Where(p => p.Code == pc)
                .Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "";

        return new FreightInvoicePrintDto
        {
            InvoiceNumber = invoice.Code,
            IssueDate = invoice.IssueDate.ToString("dd/MM/yyyy", PtBr),
            Status = invoice.Status switch
            {
                FreightInvoiceStatus.Draft => "RASCUNHO",
                FreightInvoiceStatus.Settled => "LIQUIDADA",
                _ => ""
            },
            EmitterName = branch?.LegalName ?? branch?.BranchName ?? "",
            EmitterTaxId = branch?.TaxId is { } bt ? FormatTaxId(bt, withLabel: true) : "",
            EmitterAddress = branch is null ? "" : JoinAddress(branch.Street, branch.StreetNumber, branch.Complement,
                branch.District, branch.Municipality?.Name, branch.Municipality?.StateAbbreviation ?? branch.StateCode, branch.ZipCode),
            EmitterPhone = string.IsNullOrWhiteSpace(branch?.Phone) ? "" : "Tel. " + branch.Phone,
            TakerCode = invoice.TakerCardCode,
            TakerName = partner?.CardName ?? invoice.TakerName ?? "",
            TakerTaxId = partner?.TaxId is { } tt ? FormatTaxId(tt, withLabel: true) : "",
            TakerAddress = takerAddress is null ? "" : JoinAddress(takerAddress.Street, takerAddress.StreetNumber, takerAddress.Complement,
                takerAddress.Block, takerAddress.Municipality?.Name ?? takerAddress.City,
                takerAddress.Municipality?.StateAbbreviation ?? takerAddress.State, takerAddress.ZipCode),
            PaymentCondition = paymentCondition,
            BankInfo = await BankInfoAsync(invoice.BranchCode, ct),
            TotalAmount = invoice.TotalAmount,
            TotalWeightKg = items.Sum(i => i.WeightKg),
            Observation = invoice.Observation,
            Installments = installments,
            Items = items
        };
    }

    /// <summary>
    /// A filial não tem dados bancários. Usa a conta financeira do tipo Banco, ativa, da filial (ou corporativa),
    /// só quando for a única candidata - sem sinalizador de conta padrão, escolher entre várias seria chute.
    /// </summary>
    private async Task<string> BankInfoAsync(string branchCode, CancellationToken ct)
    {
        var accounts = await db.Context.FinancialAccounts.AsNoTracking()
            .Where(a => !a.Inactive && a.Type == FinancialAccountType.Bank && a.BankCode != null
                        && (a.BranchCode == branchCode || a.BranchCode == null))
            .ToListAsync(ct);

        if (accounts.Any(a => a.BranchCode == branchCode))
            accounts = accounts.Where(a => a.BranchCode == branchCode).ToList();

        if (accounts.Count != 1)
            return "";

        var a = accounts[0];
        return $"Banco {a.BankCode} {a.BankName} · Agência {a.BankBranch} · Conta {a.BankAccountNumber}".Replace("  ", " ");
    }

    private static string Place(string? code, string? state, Dictionary<string, string> cities)
    {
        var city = code is not null && cities.TryGetValue(code, out var n) ? n : "";
        return string.IsNullOrEmpty(city) ? state ?? "" : string.IsNullOrEmpty(state) ? city : $"{city}/{state}";
    }

    private static string JoinAddress(string? street, string? number, string? complement, string? district,
        string? city, string? state, string? zip)
    {
        var line1 = string.Join(", ", new[] { street, number, complement }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var cityState = string.Join("/", new[] { city, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var parts = new[] { line1, district, cityState, string.IsNullOrWhiteSpace(zip) ? null : "CEP " + FormatZip(zip) }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join(" - ", parts);
    }

    private static string FormatZip(string zip) =>
        zip.Length == 8 && zip.All(char.IsDigit) ? $"{zip[..5]}-{zip[5..]}" : zip;

    internal static string FormatTaxId(string taxId, bool withLabel = false)
    {
        var d = new string(taxId.Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            14 => (withLabel ? "CNPJ " : "") + $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            11 => (withLabel ? "CPF " : "") + $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            _ => taxId
        };
    }
}
