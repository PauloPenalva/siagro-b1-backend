using System.Globalization;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using NFe.Classes.Informacoes;
using NFe.Classes.Informacoes.Cobranca;
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Detalhe.Tributacao;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesIbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual.Tipos;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal.Tipos;
using NFe.Classes.Informacoes.Emitente;
using NFe.Classes.Informacoes.Identificacao;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Observacoes;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Total;
using NFe.Classes.Informacoes.Total.IbsCbs;
using NFe.Classes.Informacoes.Total.IbsCbs.Cbs;
using NFe.Classes.Informacoes.Total.IbsCbs.Ibs;
using NFe.Classes.Informacoes.Transporte;
using Shared.NFe.Classes.Informacoes.InfRespTec;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using IbsCbsCst = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.Tipos.CST;
using ZeusNFe = NFe.Classes.NFe;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// <see cref="NfeIssueInput"/> → modelo da Zeus (spec §8). Não assina nem valida: isso é do
/// <c>NfeSigner</c>. As regras de "leva valor ou não" de cada grupo moram num lugar só
/// (<see cref="IcmsCarriesValues"/>, <see cref="PisCofinsCarriesValues"/>,
/// <see cref="IbsCbsCarriesValues"/>) e valem para a linha e para os totais — vBC/vICMS do total
/// diferente da soma dos itens é rejeição.
/// </summary>
public static class NfeXmlBuilder
{
    /// <summary>Literal oficial (NT 2011/002, rejeição 598) — sem acento. A Zeus não faz a troca.</summary>
    public const string HomologationRecipientName = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    private const string WithoutGtin = "SEM GTIN";
    private const int Brazil = 1058;
    private const string BrazilName = "BRASIL";

    private static readonly HashSet<string> IcmsCodesWithValues = ["00", "20", "51", "90", "900"];
    private static readonly HashSet<string> PisCofinsNotTaxed = ["04", "05", "06", "07", "08", "09"];
    private static readonly HashSet<string> IbsCbsCodesWithValues = ["000", "200"];

    public static ZeusNFe Build(NfeIssueInput input)
    {
        var noteTotal = input.Items.Sum(i => i.GrandTotal);

        if (input.Payment.PaymentMeans != PaymentMeansCodes.NoPayment
            && (input.Payment.PaidAmount != noteTotal || input.Payment.Installments.Sum(i => i.Amount) != noteTotal))
            throw new DefaultException("O total do pagamento não confere com o valor da nota.");

        var interstate = !string.Equals(
            input.Issuer.Address.State, input.Recipient.Address.State, StringComparison.OrdinalIgnoreCase);

        return new ZeusNFe
        {
            infNFe = new infNFe
            {
                versao = "4.00",
                ide = BuildIde(input, interstate),
                emit = BuildIssuer(input.Issuer),
                dest = BuildRecipient(input),
                entrega = input.Delivery is null ? null : BuildDelivery(input.Delivery),
                det = input.Items.Select(BuildItem).ToList(),
                total = BuildTotal(input.Items),
                transp = BuildTransport(input, interstate),
                cobr = BuildBilling(input),
                pag = BuildPayment(input.Payment),
                infAdic = BuildAdditionalInfo(input),
                infRespTec = input.TechnicalResponsible is { } tech
                    ? new infRespTec { CNPJ = tech.Cnpj, xContato = tech.Contact, email = tech.Email, fone = tech.Phone }
                    : null,
            },
        };
    }

    internal static bool IcmsCarriesValues(string code) => IcmsCodesWithValues.Contains(code);

    internal static bool PisCofinsCarriesValues(string cst) => !PisCofinsNotTaxed.Contains(cst);

    internal static bool IbsCbsCarriesValues(string? cst) => cst is not null && IbsCbsCodesWithValues.Contains(cst);

    private static ide BuildIde(NfeIssueInput input, bool interstate) => new()
    {
        cUF = (Estado)int.Parse(input.Issuer.Address.MunicipalityCode[..2], CultureInfo.InvariantCulture),
        cNF = input.RandomCode,
        natOp = Truncate(input.OperationNature, 60),
        mod = ModeloDocumento.NFe,
        serie = input.Series,
        nNF = input.Number,
        dhEmi = input.IssuedAt,
        dhSaiEnt = input.IssuedAt,
        tpNF = input.Direction == NfeDirection.Incoming ? TipoNFe.tnEntrada : TipoNFe.tnSaida,
        idDest = interstate ? DestinoOperacao.doInterestadual : DestinoOperacao.doInterna,
        cMunFG = long.Parse(input.Issuer.Address.MunicipalityCode, CultureInfo.InvariantCulture),
        tpImp = TipoImpressao.tiRetrato,
        tpEmis = TipoEmissao.teNormal,
        tpAmb = Environment(input.Environment),
        finNFe = input.Purpose == NfePurpose.Return ? FinalidadeNFe.fnDevolucao : FinalidadeNFe.fnNormal,
        // Consumidor final só existe na SAÍDA (regra 696): na entrada o destinatário é quem vende.
        indFinal = input.Direction == NfeDirection.Outgoing &&
                   input.Recipient.Indicator == StateRegistrationIndicator.NonTaxpayer
            ? ConsumidorFinal.cfConsumidorFinal
            : ConsumidorFinal.cfNao,
        indPres = PresencaComprador.pcOutros,
        indIntermed = IndicadorIntermediador.iiSemIntermediador,
        procEmi = ProcessoEmissao.peAplicativoContribuinte,
        verProc = Truncate(input.ApplicationVersion, 20),
        // Rejeição 1010: a referência vai num nível só. Com DFeReferenciado nos itens (VC02-14),
        // o cabeçalho fica sem NFref.
        NFref = input.ReferencedKeys.Count == 0 || input.Items.Any(item => item.Reference is not null)
            ? null
            : input.ReferencedKeys.Select(key => new NFref { refNFe = key }).ToList(),
    };

    internal static TipoAmbiente Environment(NfeEnvironment environment) =>
        environment == NfeEnvironment.Production ? TipoAmbiente.Producao : TipoAmbiente.Homologacao;

    private static emit BuildIssuer(NfeIssuer issuer)
    {
        var address = issuer.Address;
        var result = new emit
        {
            xNome = Truncate(issuer.LegalName, 60),
            xFant = TruncateOrNull(issuer.TradeName, 60),
            IE = issuer.StateRegistration,
            CRT = issuer.TaxRegime switch
            {
                TaxRegime.SimplesNacional => CRT.SimplesNacional,
                TaxRegime.SimplesNacionalExcess => CRT.SimplesNacionalExcessoSublimite,
                TaxRegime.Mei => CRT.SimplesNacionalMei,
                _ => CRT.RegimeNormal,
            },
            enderEmit = new enderEmit
            {
                xLgr = Truncate(address.Street, 60),
                nro = Truncate(address.Number, 60),
                xCpl = TruncateOrNull(address.Complement, 60),
                xBairro = Truncate(address.District, 60),
                cMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
                xMun = Truncate(address.MunicipalityName, 60),
                UF = Enum.Parse<Estado>(address.State, ignoreCase: true),
                CEP = address.ZipCode,
                cPais = Brazil,
                xPais = BrazilName,
                fone = Phone(address.Phone),
            },
        };

        if (issuer.TaxId.Length == 11)
            result.CPF = issuer.TaxId;
        else
            result.CNPJ = issuer.TaxId;

        return result;
    }

    private static dest BuildRecipient(NfeIssueInput input)
    {
        var recipient = input.Recipient;
        var address = recipient.Address;

        if (recipient.Indicator == StateRegistrationIndicator.Taxpayer && string.IsNullOrWhiteSpace(recipient.StateRegistration))
            throw new DefaultException("Destinatário contribuinte do ICMS sem inscrição estadual.");

        var result = new dest(VersaoServico.Versao400)
        {
            xNome = input.Environment == NfeEnvironment.Homologation
                ? HomologationRecipientName
                : Truncate(recipient.Name, 60),
            indIEDest = recipient.Indicator switch
            {
                StateRegistrationIndicator.Taxpayer => indIEDest.ContribuinteICMS,
                StateRegistrationIndicator.Exempt => indIEDest.Isento,
                _ => indIEDest.NaoContribuinte,
            },
            // Só o contribuinte leva IE: isento e não contribuinte não informam (schema e NT).
            IE = recipient.Indicator == StateRegistrationIndicator.Taxpayer ? recipient.StateRegistration : null,
            email = TruncateOrNull(recipient.Email, 60),
            enderDest = new enderDest
            {
                xLgr = Truncate(address.Street, 60),
                nro = Truncate(address.Number, 60),
                xCpl = TruncateOrNull(address.Complement, 60),
                xBairro = Truncate(address.District, 60),
                cMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
                xMun = Truncate(address.MunicipalityName, 60),
                UF = address.State,
                CEP = address.ZipCode,
                cPais = Brazil,
                xPais = BrazilName,
                fone = Phone(address.Phone),
            },
        };

        if (recipient.TaxId.Length == 11)
            result.CPF = recipient.TaxId;
        else
            result.CNPJ = recipient.TaxId;

        return result;
    }

    private static entrega BuildDelivery(NfeDelivery delivery)
    {
        var address = delivery.Address;
        var result = new entrega
        {
            xNome = Truncate(delivery.Name, 60),
            xLgr = Truncate(address.Street, 60),
            nro = Truncate(address.Number, 60),
            xCpl = TruncateOrNull(address.Complement, 60),
            xBairro = Truncate(address.District, 60),
            cMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
            xMun = Truncate(address.MunicipalityName, 60),
            UF = address.State,
            cPais = Brazil,
            xPais = BrazilName,
            IE = Blank(delivery.StateRegistration),
        };

        if (!string.IsNullOrWhiteSpace(address.ZipCode))
            result.CEP = long.Parse(address.ZipCode, CultureInfo.InvariantCulture);

        if (delivery.TaxId.Length == 11)
            result.CPF = delivery.TaxId;
        else
            result.CNPJ = delivery.TaxId;

        return result;
    }

    private static det BuildItem(NfeItem item) => new()
    {
        nItem = item.Number,
        prod = new prod
        {
            cProd = Truncate(item.ItemCode, 60),
            cEAN = WithoutGtin,
            xProd = Truncate(item.Description, 120),
            NCM = item.Ncm,
            CEST = Blank(item.Cest),
            // Com CEST, a escala vai como relevante (S) — o caso "N" exige o CNPJ do fabricante.
            indEscala = Blank(item.Cest) is null ? null : indEscala.S,
            cBenef = Blank(item.BenefitCode),
            CFOP = int.Parse(item.Cfop, CultureInfo.InvariantCulture),
            uCom = Truncate(item.UnitOfMeasure, 6),
            qCom = item.Quantity,
            vUnCom = item.UnitPrice,
            vProd = item.Total,
            cEANTrib = WithoutGtin,
            uTrib = Truncate(item.UnitOfMeasure, 6),
            qTrib = item.Quantity,
            vUnTrib = item.UnitPrice,
            // Frete, seguro, desconto e outras despesas da linha: omitidos quando 0 (o leiaute permite, e a nota sem eles
            // sai idêntica à de antes).
            vFrete = Charge(item.FreightValue),
            vSeg = Charge(item.InsuranceValue),
            vDesc = Charge(item.DiscountValue),
            vOutro = Charge(item.OtherExpensesValue),
            indTot = IndicadorTotal.ValorDoItemCompoeTotalNF,
        },
        imposto = new imposto
        {
            ICMS = new ICMS { TipoICMS = BuildIcms(item) },
            PIS = new PIS { TipoPIS = BuildPis(item) },
            COFINS = new COFINS { TipoCOFINS = BuildCofins(item) },
            IBSCBS = BuildIbsCbs(item),
        },
        // VC02-14: cada item da devolução aponta o item da venda (chave + nItem da nota original).
        DFeReferenciado = item.Reference is { } reference
            ? new DFeReferenciado { chaveAcesso = reference.AccessKey, nItem = reference.ItemNumber }
            : null,
    };

    /// <summary>Valor da linha no <c>det/prod</c>: nulo (não serializado) quando zero.</summary>
    private static decimal? Charge(decimal value) => value == 0m ? null : value;

    private static ICMSBasico BuildIcms(NfeItem item)
    {
        var origin = (OrigemMercadoria)item.GoodsOrigin;
        decimal? reduction = item.IcmsBaseReduction > 0 ? item.IcmsBaseReduction : null;

        return item.IcmsCode switch
        {
            "00" => new ICMS00
            {
                orig = origin, CST = Csticms.Cst00, modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            "20" => new ICMS20
            {
                orig = origin, CST = Csticms.Cst20, modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                pRedBC = item.IcmsBaseReduction, vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            "40" => new ICMS40 { orig = origin, CST = Csticms.Cst40 },
            "41" => new ICMS40 { orig = origin, CST = Csticms.Cst41 },
            "50" => new ICMS40 { orig = origin, CST = Csticms.Cst50 },
            "51" => new ICMS51
            {
                orig = origin, CST = Csticms.Cst51, modBC = DeterminacaoBaseIcms.DbiValorOperacao, pRedBC = reduction,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMSOp = item.IcmsOperationValue,
                pDif = item.IcmsDeferral, vICMSDif = item.IcmsDeferredValue, vICMS = item.IcmsValue,
            },
            "90" => new ICMS90
            {
                orig = origin, CST = Csticms.Cst90, modBC = DeterminacaoBaseIcms.DbiValorOperacao, pRedBC = reduction,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            "102" or "103" or "300" or "400" => new ICMSSN102
            {
                orig = origin, CSOSN = Enum.Parse<Csosnicms>("Csosn" + item.IcmsCode),
            },
            "900" => new ICMSSN900
            {
                orig = origin, CSOSN = Csosnicms.Csosn900, modBC = DeterminacaoBaseIcms.DbiValorOperacao, pRedBC = reduction,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            _ => throw new DefaultException(
                $"O item {item.ItemCode} tem o código de ICMS {item.IcmsCode}, que este sistema não emite."),
        };
    }

    private static PISBasico BuildPis(NfeItem item)
    {
        var cst = Enum.Parse<CSTPIS>("pis" + item.PisCst);

        if (item.PisCst is "01" or "02")
            return new PISAliq { CST = cst, vBC = item.PisBase, pPIS = item.PisRate, vPIS = item.PisValue };

        if (!PisCofinsCarriesValues(item.PisCst))
            return new PISNT { CST = cst };

        EnsureOutrCst(item, item.PisCst);

        return new PISOutr { CST = cst, vBC = item.PisBase, pPIS = item.PisRate, vPIS = item.PisValue };
    }

    private static COFINSBasico BuildCofins(NfeItem item)
    {
        var cst = Enum.Parse<CSTCOFINS>("cofins" + item.CofinsCst);

        if (item.CofinsCst is "01" or "02")
            return new COFINSAliq { CST = cst, vBC = item.CofinsBase, pCOFINS = item.CofinsRate, vCOFINS = item.CofinsValue };

        if (!PisCofinsCarriesValues(item.CofinsCst))
            return new COFINSNT { CST = cst };

        EnsureOutrCst(item, item.CofinsCst);

        return new COFINSOutr { CST = cst, vBC = item.CofinsBase, pCOFINS = item.CofinsRate, vCOFINS = item.CofinsValue };
    }

    /// <summary>O grupo Outr só vale para o CST 49 e de 50 a 99; os demais não têm grupo aqui.</summary>
    private static void EnsureOutrCst(NfeItem item, string cst)
    {
        var valid = cst == "49" || (int.TryParse(cst, CultureInfo.InvariantCulture, out var number) && number is >= 50 and <= 99);

        if (!valid)
            throw new DefaultException($"O item {item.ItemCode} tem o CST de PIS/COFINS {cst}, que este sistema não emite.");
    }

    private static IBSCBS? BuildIbsCbs(NfeItem item)
    {
        if (item.IbsCbsCst is null)
            return null;

        var group = new IBSCBS
        {
            CST = Enum.Parse<IbsCbsCst>("Cst" + item.IbsCbsCst),
            cClassTrib = item.IbsCbsClassCode,
        };

        if (!IbsCbsCarriesValues(item.IbsCbsCst))
            return group;

        group.gIBSCBS = new gIBSCBS
        {
            vBC = item.IbsCbsBase,
            gIBSUF = new gIBSUF
            {
                pIBSUF = item.IbsStateRate,
                gRed = Reduction(item.IbsStateRate, item.IbsRateReduction),
                vIBSUF = item.IbsStateValue,
            },
            gIBSMun = new gIBSMun
            {
                pIBSMun = item.IbsMunicipalRate,
                gRed = Reduction(item.IbsMunicipalRate, item.IbsRateReduction),
                vIBSMun = item.IbsMunicipalValue,
            },
            vIBS = item.IbsStateValue + item.IbsMunicipalValue,
            gCBS = new gCBS
            {
                pCBS = item.CbsRate,
                gRed = Reduction(item.CbsRate, item.CbsRateReduction),
                vCBS = item.CbsValue,
            },
        };

        return group;
    }

    /// <summary>Redução de alíquota: <c>pAliqEfet</c> = alíquota × (1 − redução/100), 4 casas.</summary>
    private static gRed? Reduction(decimal rate, decimal reduction) =>
        reduction <= 0
            ? null
            : new gRed
            {
                pRedAliq = reduction,
                pAliqEfet = decimal.Round(rate * (1 - reduction / 100m), 4, MidpointRounding.AwayFromZero),
            };

    private static total BuildTotal(IReadOnlyList<NfeItem> items)
    {
        var products = items.Sum(i => i.Total);
        var freight = items.Sum(i => i.FreightValue);
        var insurance = items.Sum(i => i.InsuranceValue);
        var discount = items.Sum(i => i.DiscountValue);
        var otherExpenses = items.Sum(i => i.OtherExpensesValue);
        var withIcms = items.Where(i => IcmsCarriesValues(i.IcmsCode)).ToList();
        var withIbsCbs = items.Where(i => IbsCbsCarriesValues(i.IbsCbsCst)).ToList();

        return new total
        {
            ICMSTot = new ICMSTot
            {
                vBC = withIcms.Sum(i => i.IcmsBase),
                vICMS = withIcms.Sum(i => i.IcmsValue),
                vICMSDeson = 0,
                vFCP = 0,
                vBCST = 0,
                vST = 0,
                vFCPST = 0,
                vFCPSTRet = 0,
                vProd = products,
                vFrete = freight,
                vSeg = insurance,
                vDesc = discount,
                vII = 0,
                vIPI = 0,
                vIPIDevol = 0,
                vPIS = items.Where(i => PisCofinsCarriesValues(i.PisCst)).Sum(i => i.PisValue),
                vCOFINS = items.Where(i => PisCofinsCarriesValues(i.CofinsCst)).Sum(i => i.CofinsValue),
                vOutro = otherExpenses,
                // W16: vNF = vProd − vDesc + vFrete + vSeg + vOutro (sem ST, IPI, II e serviços, que o Siagro não trata).
                vNF = products + freight + insurance + otherExpenses - discount,
            },
            IBSCBSTot = items.Any(i => i.IbsCbsCst is not null)
                ? new IBSCBSTot
                {
                    vBCIBSCBS = withIbsCbs.Sum(i => i.IbsCbsBase),
                    gIBS = new gIBS
                    {
                        gIBSUF = new gIBSUFTotal { vDif = 0, vDevTrib = 0, vIBSUF = withIbsCbs.Sum(i => i.IbsStateValue) },
                        gIBSMun = new gIBSMunTotal { vDif = 0, vDevTrib = 0, vIBSMun = withIbsCbs.Sum(i => i.IbsMunicipalValue) },
                        vIBS = withIbsCbs.Sum(i => i.IbsStateValue + i.IbsMunicipalValue),
                        vCredPres = 0,
                        vCredPresCondSus = 0,
                    },
                    gCBS = new gCBSTotal
                    {
                        vDif = 0, vDevTrib = 0, vCBS = withIbsCbs.Sum(i => i.CbsValue), vCredPres = 0, vCredPresCondSus = 0,
                    },
                }
                : null,
        };
    }

    private static transp BuildTransport(NfeIssueInput input, bool interstate)
    {
        var result = new transp
        {
            modFrete = input.FreightTerms switch
            {
                FreightTerms.Cif => ModalidadeFrete.mfContaEmitenteOumfContaRemetente,
                FreightTerms.Fob => ModalidadeFrete.mfContaDestinatario,
                FreightTerms.Ter => ModalidadeFrete.mfContaTerceiros,
                _ => ModalidadeFrete.mfSemFrete,
            },
        };

        if (input.FreightTerms != FreightTerms.None && input.Carrier is { } carrier)
        {
            result.transporta = new transporta
            {
                xNome = Truncate(carrier.Name, 60),
                IE = Blank(carrier.StateRegistration),
                xEnder = carrier.FullAddress is null ? null : Truncate(carrier.FullAddress, 60),
                xMun = TruncateOrNull(carrier.MunicipalityName, 60),
                UF = Blank(carrier.State),
            };

            if (carrier.TaxId is { Length: 11 })
                result.transporta.CPF = carrier.TaxId;
            else if (!string.IsNullOrWhiteSpace(carrier.TaxId))
                result.transporta.CNPJ = carrier.TaxId;
        }

        // Veículo só em operação interna (spec §8 e risco §14): na interestadual a SEFAZ rejeita.
        if (input.FreightTerms != FreightTerms.None && !interstate && input.Vehicle is { } vehicle)
            result.veicTransp = new veicTransp { placa = vehicle.Plate, UF = vehicle.State };

        // Os pesos são exigidos na prontidão; quantidade, espécie, marca e numeração vão só se
        // informados no documento (granel normalmente não tem).
        if (input.NetWeight > 0 || input.GrossWeight > 0)
            result.vol =
            [
                new vol
                {
                    qVol = input.Volume?.Quantity,
                    esp = TruncateOrNull(input.Volume?.Species, 60),
                    marca = TruncateOrNull(input.Volume?.Brand, 60),
                    nVol = TruncateOrNull(input.Volume?.Numbering, 60),
                    pesoL = input.NetWeight,
                    pesoB = input.GrossWeight,
                },
            ];

        return result;
    }

    private static cobr? BuildBilling(NfeIssueInput input)
    {
        if (input.Payment.Installments.Count == 0)
            return null;

        // Rejeição 853: pagamento à vista (indPag 0) não pode informar dados de cobrança (fat/dup).
        if (input.Payment.PaymentIndicator == 0)
            return null;

        var total = input.Items.Sum(i => i.GrandTotal);

        return new cobr
        {
            fat = new fat { nFat = input.BillingNumber, vOrig = total, vDesc = 0, vLiq = total },
            dup = input.Payment.Installments
                .Select(i => new dup
                {
                    nDup = i.Number.ToString("000", CultureInfo.InvariantCulture),
                    dVenc = i.DueDate.ToDateTime(TimeOnly.MinValue),
                    vDup = i.Amount,
                })
                .ToList(),
        };
    }

    private static List<pag> BuildPayment(PaymentPlan plan) =>
    [
        new pag
        {
            detPag =
            [
                new detPag
                {
                    indPag = plan.PaymentIndicator switch
                    {
                        0 => IndicadorPagamentoDetalhePagamento.ipDetPgVista,
                        1 => IndicadorPagamentoDetalhePagamento.ipDetPgPrazo,
                        _ => null,
                    },
                    tPag = (FormaPagamento)int.Parse(plan.PaymentMeans, CultureInfo.InvariantCulture),
                    // tPag 99 exige a descrição (NT 2020.006).
                    xPag = plan.PaymentMeans == PaymentMeansCodes.Other ? "Outros" : null,
                    vPag = plan.PaidAmount,
                },
            ],
        },
    ];

    private static infAdic? BuildAdditionalInfo(NfeIssueInput input)
    {
        var complementary = Blank(input.AdditionalInfo);
        var fisco = Blank(input.FiscoInfo);

        if (complementary is null && fisco is null)
            return null;

        return new infAdic
        {
            infCpl = complementary is null ? null : Truncate(complementary, 5000),
            infAdFisco = fisco is null ? null : Truncate(fisco, 2000),
        };
    }

    private static string? TruncateOrNull(string? value, int length) =>
        Blank(value) is { } text ? Truncate(text, length) : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int length)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= length ? trimmed : trimmed[..length].TrimEnd();
    }

    private static long? Phone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        return digits.Length is >= 6 and <= 14 ? long.Parse(digits, CultureInfo.InvariantCulture) : null;
    }
}
