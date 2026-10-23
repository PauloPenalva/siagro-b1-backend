using System.Globalization;
using System.Reflection;
using System.Xml.Serialization;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using MDFe.Classes.Flags;
using MDFe.Classes.Informacoes;
using SiagroB1.Fiscal.Dfe;
using SiagroB1.Fiscal.Nfe;
using VersaoServico = MDFe.Utils.Flags.VersaoServico;
using ZeusMdfe = MDFe.Classes.Informacoes.MDFe;

namespace SiagroB1.Fiscal.Mdfe;

/// <summary>
/// Traduz <see cref="MdfeIssueInput"/> para o modelo da Zeus MDF-e 3.00: modal rodoviário,
/// emitente prestador de serviço de transporte (tpEmit 1, sem tpTransp), emissão normal. A chave e
/// o <c>cDV</c> saem de <see cref="MdfeAccessKey"/>; a assinatura fica com <see cref="MdfeSigner"/>.
/// </summary>
public static class MdfeXmlBuilder
{
    private const string ProcessVersion = "SiagroB1";
    private const int NormalEmission = 1;

    public static ZeusMdfe Build(MdfeIssueInput input)
    {
        MdfeZeusConfiguration.PinGlobalLayout();

        var issuerState = State(input.Issuer.Address.State);
        var key = MdfeAccessKey.Build(
            ((int)issuerState).ToString("00", CultureInfo.InvariantCulture), input.IssuedAt.DateTime,
            input.Issuer.TaxId, input.Series, input.Number, NormalEmission, input.RandomCode);
        var environment = NfeXmlBuilder.Environment(input.Environment);

        return new ZeusMdfe
        {
            InfMDFe = new MDFeInfMDFe
            {
                Versao = VersaoServico.Versao300,
                Id = "MDFe" + key,
                Ide = BuildIde(input, issuerState, environment, key),
                Emit = BuildIssuer(input),
                InfModal = new MDFeInfModal { VersaoModal = MDFeVersaoModal.Versao300, Modal = BuildRoad(input) },
                InfDoc = new MDFeInfDoc
                {
                    InfMunDescarga = input.UnloadingCities.Select(c => new MDFeInfMunDescarga
                    {
                        CMunDescarga = c.IbgeCode,
                        XMunDescarga = DfeText.Clean(c.Name, 60),
                        InfCTe = c.CteKeys.Select(k => new MDFeInfCTe { ChCTe = k }).ToList(),
                    }).ToList(),
                },
                Seg = input.Insurance is { } insurance ? [BuildInsurance(insurance)] : null,
                ProdPred = BuildProduct(input),
                Tot = new MDFeTot
                {
                    QCTe = input.Totals.CteCount,
                    vCarga = input.Totals.CargoValue,
                    CUnid = Code<MDFeCUnid>(input.Totals.WeightUnit),
                    QCarga = input.Totals.GrossWeight,
                },
                Lacres = input.Seals.Count == 0 ? null : input.Seals.Select(s => new MDFeLacre { NLacre = DfeText.Clean(s, 60) }).ToList(),
                InfAdic = DfeText.CleanOrNull(input.AdditionalInfo, 5000) is { } info ? new MDFeInfAdic { InfCpl = info } : null,
                InfRespTec = input.InfRespTec is { } tech
                    ? new MDFeInfRespTec
                    {
                        CNPJ = tech.Cnpj,
                        XContato = DfeText.Clean(tech.Contact, 60),
                        Email = DfeText.Clean(tech.Email, 60),
                        Fone = Digits(tech.Phone),
                    }
                    : null,
            },
            InfMDFeSupl = new MdfeInfMDFeSupl { QrCodMDFe = MdfeInfMDFeSupl.GerarQrCode(key, environment) },
        };
    }

    private static MDFeIde BuildIde(MdfeIssueInput input, Estado issuerState, TipoAmbiente environment, string key) =>
        new(VersaoServico.Versao300)
        {
            CUF = issuerState,
            TpAmb = environment,
            TpEmit = MDFeTipoEmitente.PrestadorServicoDeTransporte,
            Mod = ModeloDocumento.MDFe,
            Serie = checked((short)input.Series),
            NMDF = input.Number,
            CMDF = int.Parse(input.RandomCode, CultureInfo.InvariantCulture),
            CDV = (byte)(key[43] - '0'),
            Modal = MDFeModal.Rodoviario,
            DhEmi = input.IssuedAt.DateTime,
            TpEmis = MDFeTipoEmissao.Normal,
            ProcEmi = MDFeIdentificacaoProcessoEmissao.EmissaoComAplicativoContribuinte,
            VerProc = ProcessVersion,
            UFIni = State(input.UfStart),
            UFFim = State(input.UfEnd),
            InfMunCarrega = input.LoadingCities
                .Select(c => new MDFeInfMunCarrega { CMunCarrega = c.IbgeCode, XMunCarrega = DfeText.Clean(c.Name, 60) })
                .ToList(),
            InfPercurso = input.RouteUfs.Count == 0
                ? null
                : input.RouteUfs.Select(uf => new MDFeInfPercurso { UFPer = State(uf) }).ToList(),
        };

    private static MDFeEmit BuildIssuer(MdfeIssueInput input)
    {
        var issuer = input.Issuer;
        var address = issuer.Address;
        return new MDFeEmit
        {
            CNPJ = issuer.TaxId,
            IE = DfeText.Clean(issuer.StateRegistration),
            // Gancho da homologação: nulo por padrão, o nome vai como está no cadastro.
            XNome = DfeText.Clean(input.IssuerNameOverride ?? issuer.LegalName, 60),
            XFant = DfeText.CleanOrNull(issuer.TradeName, 60),
            EnderEmit = new MDFeEnderEmit
            {
                XLgr = DfeText.Clean(address.Street, 60),
                Nro = DfeText.Clean(address.Number, 60),
                XCpl = DfeText.CleanOrNull(address.Complement, 60),
                XBairro = DfeText.Clean(address.District, 60),
                CMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
                XMun = DfeText.Clean(address.MunicipalityName, 60),
                CEP = string.IsNullOrWhiteSpace(address.ZipCode) ? 0 : long.Parse(Digits(address.ZipCode)!, CultureInfo.InvariantCulture),
                UF = State(address.State),
            },
        };
    }

    private static MDFeRodo BuildRoad(MdfeIssueInput input) => new()
    {
        InfANTT = new MDFeInfANTT
        {
            RNTRC = input.Issuer.Rntrc,
            InfCIOT = input.Ciot is { } ciot ? [Ciot(ciot)] : null,
            ValePed = input.TollVoucher is { } toll
                ? new MDFeValePed
                {
                    Disp = [new MDFeDisp { CNPJForn = toll.SupplierCnpj, NCompra = DfeText.Clean(toll.Number, 20), VValePed = toll.Value }],
                }
                : null,
            InfContratante = input.Contractors.Count == 0 ? null : input.Contractors.Select(Contractor).ToList(),
            InfPag = input.Payment is { } payment ? [BuildPayment(payment)] : null,
        },
        VeicTracao = new MDFeVeicTracao
        {
            CInt = null,
            Placa = Plate(input.Traction.Plate),
            RENAVAM = DfeText.CleanOrNull(input.Traction.Renavam),
            Tara = input.Traction.TareKg,
            CapKG = input.Traction.CapacityKg,
            CapM3 = input.Traction.CapacityM3,
            Condutor = input.Drivers.Select(d => new MDFeCondutor { XNome = DfeText.Clean(d.Name, 60), CPF = d.Cpf }).ToList(),
            TpRod = Code<MDFeTpRod>(input.Traction.WheelType),
            TpCar = Code<MDFeTpCar>(input.Traction.BodyType),
            UF = State(input.Traction.Uf),
        },
        VeicReboque = input.Trailers.Count == 0
            ? null
            : input.Trailers.Select(t => new MDFeVeicReboque
            {
                Placa = Plate(t.Plate),
                RENAVAM = DfeText.CleanOrNull(t.Renavam),
                Tara = t.TareKg,
                CapKG = t.CapacityKg,
                CapM3 = t.CapacityM3,
                TpCar = Code<MDFeTpCar>(t.BodyType),
                UF = State(t.Uf),
            }).ToList(),
    };

    private static infCIOT Ciot(MdfeCiot ciot) => ciot.ResponsibleTaxId.Length == 11
        ? new infCIOT { CIOT = ciot.Number, CPF = ciot.ResponsibleTaxId }
        : new infCIOT { CIOT = ciot.Number, CNPJ = ciot.ResponsibleTaxId };

    private static MDFeInfContratante Contractor(MdfeContractor c) => c.TaxId.Length == 11
        ? new MDFeInfContratante { XNome = DfeText.CleanOrNull(c.Name, 60), CPF = c.TaxId }
        : new MDFeInfContratante { XNome = DfeText.CleanOrNull(c.Name, 60), CNPJ = c.TaxId };

    private static MDFeInfPag BuildPayment(MdfePayment payment)
    {
        var contractor = payment.Contractor;
        var pag = new MDFeInfPag
        {
            XNome = DfeText.CleanOrNull(contractor.Name, 60),
            Comp = payment.Components.Select(c => new MDFeComp
            {
                TpComp = Code<MDFeTpComp>(c.Type),
                VComp = c.Value,
                XComp = DfeText.CleanOrNull(c.Description, 60),
            }).ToList(),
            VContratoProxy = payment.Total,
            IndPag = payment.IndPag == 1 ? MDFeIndPag.PagamentoPrazo : MDFeIndPag.PagamentoVista,
            InfPrazo = payment.Installments.Count == 0
                ? null
                : payment.Installments.Select(i => new MDFeInfPrazo
                {
                    NParcelaProxy = i.Number.ToString("000", CultureInfo.InvariantCulture),
                    DVencProxy = i.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    VParcelaProxy = i.Value,
                }).ToList(),
            InfBanc = DfeText.CleanOrNull(payment.Pix, 60) is { } pix
                ? new MDFeInfBanc { PIX = pix }
                : new MDFeInfBanc { CodBanco = DfeText.Clean(payment.BankCode, 5), CodAgencia = DfeText.Clean(payment.Agency, 10) },
        };

        if (contractor.TaxId.Length == 11)
            pag.CPF = contractor.TaxId;
        else
            pag.CNPJ = contractor.TaxId;

        return pag;
    }

    private static MDFeSeg BuildInsurance(MdfeInsurance insurance)
    {
        var responsible = new MDFeInfResp
        {
            RespSeg = insurance.Responsible == 2 ? MDFeRespSeg.ResponsavelPelaContratacao : MDFeRespSeg.EmitenteDoMDFe,
        };
        if (insurance.Responsible == 2 && insurance.ResponsibleTaxId is { } taxId)
        {
            if (taxId.Length == 11)
                responsible.CPF = taxId;
            else
                responsible.CNPJ = taxId;
        }

        return new MDFeSeg
        {
            InfResp = responsible,
            InfSeg = new MDFeInfSeg { XSeg = DfeText.Clean(insurance.InsurerName, 30), CNPJ = insurance.InsurerCnpj },
            NApol = DfeText.CleanOrNull(insurance.PolicyNumber, 20),
            NAver = insurance.Endorsements.Count == 0 ? null : insurance.Endorsements.Select(e => DfeText.Clean(e, 40)).ToList(),
        };
    }

    private static MDFeProdPred BuildProduct(MdfeIssueInput input)
    {
        var product = input.Product;
        var loadingZip = Digits(product.LoadingZip);
        var unloadingZip = Digits(product.UnloadingZip);

        // infLotacao: carga lotação — um único CT-e, com os CEPs de carga e descarga conhecidos.
        var lotacao = input.Totals.CteCount == 1 && loadingZip is not null && unloadingZip is not null
            ? new MDFeInfLotacao
            {
                InfLocalCarrega = new MDFeInfLocalCarrega { CEP = loadingZip },
                InfLocalDescarrega = new MDFeInfLocalDescarrega { CEP = unloadingZip },
            }
            : null;

        return new MDFeProdPred
        {
            TpCarga = Code<MDFeTpCarga>(product.CargoType),
            XProd = DfeText.Clean(product.Description, 120),
            Ncm = Digits(product.Ncm),
            InfLotacao = lotacao,
        };
    }

    private static string Plate(string plate) =>
        new string(plate.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string? Digits(string? value)
    {
        if (value is null)
            return null;
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static Estado State(string uf) => Enum.Parse<Estado>(uf.Trim(), ignoreCase: true);

    /// <summary>Converte o código do XML ("01", "99"…) no valor do enum da Zeus pelo <c>XmlEnum</c>.</summary>
    private static T Code<T>(string code) where T : struct, Enum
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetCustomAttribute<XmlEnumAttribute>()?.Name == code)
                return (T)field.GetValue(null)!;
        }

        throw new ArgumentException($"Código \"{code}\" inválido para {typeof(T).Name}.");
    }
}
