using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ContractDrafts;

/// <summary>
/// Monta o dicionário de placeholders a partir do contrato. Todos os valores saem já formatados
/// em pt-BR; ausência vira string vazia, nunca exceção — o modelo é do usuário e ele decide o
/// que citar. Parceiro SEMPRE por <see cref="IBusinessPartnerService"/>: em modo SAPB1 a tabela
/// local BUSINESS_PARTNERS está vazia e a navegação zeraria tudo.
/// </summary>
public class ContractDraftPlaceholderResolver(AppDbContext context, IBusinessPartnerService partners)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<Dictionary<string, string>> ResolveAsync(PurchaseContract c, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var partner = await partners.GetByIdAsync(c.CardCode);
        var total = decimal.Round(c.TotalVolume * c.StandardPrice, 2, MidpointRounding.ToEven);

        await AddCommonAsync(values, c.BranchCode, c.Code, c.Complement, c.CreationDate, c.DeliveryStartDate,
            c.DeliveryEndDate, c.AgentName, c.DeliveryLocationName, c.ItemName, c.HarvestSeasonCode,
            c.TotalVolume, c.UnitOfMeasureCode, c.StandardPrice, c.StandardCurrency, total, c.FreightTerms,
            c.PaymentTerms, c.StandardCashFlowDate, c.CardCode, ct);

        AddPartner(values, "fornecedor", partner);

        var broker = c.Brokers.FirstOrDefault();
        values["corretor_nome"] = broker?.CardName ?? "";
        values["corretor_comissao"] = broker is null ? "" : Number(broker.Commission, 2) + (broker.ComissionUmCode ?? "");

        return values;
    }

    public async Task<Dictionary<string, string>> ResolveAsync(SalesContract c, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var partner = await partners.GetByIdAsync(c.CardCode);
        var total = decimal.Round(c.Volume * c.Price, 2, MidpointRounding.ToEven);
        var locations = string.Join("; ", c.DeliveryLocations.Select(l => l.CardName).Where(n => !string.IsNullOrWhiteSpace(n)));

        await AddCommonAsync(values, c.BranchCode, c.Code, c.Complement, c.CreationDate, c.DeliveryStartDate,
            c.DeliveryEndDate, c.AgentName, locations, c.ItemName, c.HarvestSeasonCode,
            c.Volume, c.UnitOfMeasureCode, c.Price, c.StandardCurrency, total, c.FreightTerms,
            c.PaymentTerms, c.StandardCashFlowDate, c.CardCode, ct);

        AddPartner(values, "cliente", partner);

        return values;
    }

    private async Task AddCommonAsync(
        Dictionary<string, string> v, string? branchCode, string? code, string? complement, DateTime? issued,
        DateTime deliveryStart, DateTime deliveryEnd, string? agentName, string? deliveryLocation, string? itemName,
        string? harvestSeasonCode, decimal volume, string? uom, decimal price, CurrencyType? currency, decimal total,
        FreightTerms freight, string? paymentTerms, DateTime? cashFlowDate, string cardCode, CancellationToken ct)
    {
        var branch = branchCode is null ? null
            : await context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode, ct);
        var season = harvestSeasonCode is null ? null
            : await context.HarvestSeasons.AsNoTracking().FirstOrDefaultAsync(h => h.Code == harvestSeasonCode, ct);

        v["numero"] = code ?? "";
        v["complemento"] = complement ?? "";
        v["emissao"] = Date(issued);
        v["emissao_extenso"] = issued is null ? "" : ContractDraftNumberSpeller.Date(issued.Value);
        v["data_inicio_entrega"] = Date(deliveryStart);
        v["data_termino_entrega"] = Date(deliveryEnd);
        v["representante_nome"] = agentName ?? "";
        v["local_entrega"] = deliveryLocation ?? "";
        v["produto_descricao"] = itemName ?? "";
        v["safra_descricao"] = season?.Name ?? harvestSeasonCode ?? "";
        v["quantidade"] = Number(volume, 3);
        v["unidade_medida"] = uom ?? "";
        v["preco"] = Price(price);
        v["moeda"] = currency == CurrencyType.Usd ? "US$" : "R$";
        v["valor_total"] = Number(total, 2);
        v["valor_total_extenso"] = ContractDraftNumberSpeller.Currency(total);
        v["tipo_frete"] = freight switch
        {
            FreightTerms.Cif => "CIF", FreightTerms.Fob => "FOB", FreightTerms.Ter => "Terceiro", _ => "Nenhum",
        };
        v["condicao_pagamento"] = paymentTerms ?? "";
        v["data_pagamento"] = Date(cashFlowDate);
        v["empresa_razao_social"] = branch?.BranchName ?? "";
        v["empresa_cnpj"] = TaxId(branch?.TaxId);
        v["filial_nome"] = branch?.BranchName ?? branch?.ShortName ?? "";

        var companySigners = await context.CompanySignatories.AsNoTracking()
            .Where(s => s.Active && (s.BranchCode == null || s.BranchCode == branchCode))
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new SignerLine(s.Name, s.TaxId, s.Role)).ToListAsync(ct);
        var partnerSigners = await context.BusinessPartnerSignatories.AsNoTracking()
            .Where(s => s.Active && s.CardCode == cardCode)
            .OrderBy(s => s.Order).ThenBy(s => s.Name)
            .Select(s => new SignerLine(s.Name, s.TaxId, s.Role)).ToListAsync(ct);

        v["assinaturas_empresa"] = SignatureBlock(companySigners);
        v["assinaturas_parceiro"] = SignatureBlock(partnerSigners);
    }

    private static void AddPartner(Dictionary<string, string> v, string prefix, BusinessPartnerModel? partner)
    {
        // Endereço de faturamento ("B") quando houver; senão o primeiro. Em SAPB1 vem só um.
        var address = partner?.Addresses.FirstOrDefault(a => a.AdresType == "B") ?? partner?.Addresses.FirstOrDefault();

        v[$"{prefix}_razao_social"] = partner?.CardName ?? "";
        v[$"{prefix}_nome_fantasia"] = partner?.CardFName ?? "";
        v[$"{prefix}_cnpj"] = TaxId(partner?.TaxId);
        v[$"{prefix}_endereco"] = address?.Street ?? "";
        v[$"{prefix}_bairro"] = address?.Block ?? "";
        v[$"{prefix}_cep"] = ZipCode(address?.ZipCode);
        v[$"{prefix}_cidade"] = address?.City ?? "";
        v[$"{prefix}_uf"] = address?.State ?? "";
    }

    private record SignerLine(string Name, string TaxId, SignatoryRole Role);

    /// <summary>Uma tabela por signatário, no molde da Tagui, com nome, CPF e papel escapados.</summary>
    private static string SignatureBlock(IEnumerable<SignerLine> signers)
    {
        var sb = new StringBuilder();
        foreach (var s in signers)
        {
            sb.Append("<table class=\"signature\"><tr><td>ASSINATURA:</td><td>&nbsp;</td></tr>")
              .Append("<tr><td>").Append(HtmlEscape(RoleLabel(s.Role))).Append(":</td><td>")
              .Append(HtmlEscape(s.Name)).Append("</td></tr>")
              .Append("<tr><td>CPF:</td><td>").Append(TaxId(s.TaxId)).Append("</td></tr></table>");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escapa só o que é sintaticamente perigoso em HTML (&amp;, &lt;, &gt;, &quot;). Deliberadamente
    /// NÃO usa <c>WebUtility.HtmlEncode</c>: ele converte acentos pt-BR (ex.: "ã") em entidades
    /// numéricas/nomeadas, o que corrompe nomes de signatário no texto exibido ao usuário — a saída
    /// já é UTF-8.
    /// </summary>
    private static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public static string RoleLabel(SignatoryRole role) => role switch
    {
        SignatoryRole.Sign => "Assinar",
        SignatoryRole.Approve => "Aprovador",
        SignatoryRole.Acknowledge => "Reconhecer",
        SignatoryRole.SignAsParty => "Parte",
        SignatoryRole.SignAsWitness => "Testemunha",
        SignatoryRole.SignAsIntervening => "Interveniente",
        SignatoryRole.AcknowledgeReceipt => "Acusar recebimento",
        SignatoryRole.SignAsIssuerEndorserGuarantor => "Emissor, endossante e avalista",
        SignatoryRole.SignAsIssuerEndorserGuarantorSurety => "Emissor, endossante, avalista e fiador",
        SignatoryRole.SignAsSurety => "Fiador",
        SignatoryRole.SignAsPartyAndSurety => "Parte e fiador",
        SignatoryRole.SignAsJointDebtor => "Responsável solidário",
        SignatoryRole.SignAsPartyAndJointDebtor => "Parte e responsável solidário",
        _ => role.ToString(),
    };

    private static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", PtBr) ?? "";
    private static string Number(decimal n, int decimals) => n.ToString($"N{decimals}", PtBr);

    /// <summary>Preço com no mínimo 2 casas e no máximo 8, sem zeros à direita além da segunda casa.</summary>
    private static string Price(decimal n)
    {
        var s = n.ToString("N8", PtBr).TrimEnd('0');
        var comma = s.IndexOf(',');
        return s.Length - comma - 1 < 2 ? n.ToString("N2", PtBr) : s;
    }

    private static string TaxId(string? digits)
    {
        var d = new string((digits ?? "").Where(char.IsDigit).ToArray());
        return d.Length switch
        {
            14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
            11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
            _ => digits ?? "",
        };
    }

    private static string ZipCode(string? digits)
    {
        var d = new string((digits ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 8 ? $"{d[..5]}-{d[5..]}" : digits ?? "";
    }
}
