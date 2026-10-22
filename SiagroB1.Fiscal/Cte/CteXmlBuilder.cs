using System.Globalization;
using CTe.Classes.Informacoes;
using CTe.Classes.Informacoes.Complemento;
using CTe.Classes.Informacoes.Destinatario;
using CTe.Classes.Informacoes.Emitente;
using CTe.Classes.Informacoes.Expedidor;
using CTe.Classes.Informacoes.Identificacao;
using CTe.Classes.Informacoes.Impostos;
using CTe.Classes.Informacoes.Impostos.IBSCBS;
using CTe.Classes.Informacoes.Impostos.ICMS;
using CTe.Classes.Informacoes.Impostos.ICMS.Tipos;
using CTe.Classes.Informacoes.Impostos.Tributacao;
using CTe.Classes.Informacoes.infCTeNormal;
using CTe.Classes.Informacoes.infCTeNormal.infCargas;
using CTe.Classes.Informacoes.infCTeNormal.infModals;
using CTe.Classes.Informacoes.Recebedor;
using CTe.Classes.Informacoes.Remetente;
using CTe.Classes.Informacoes.Tipos;
using CTe.Classes.Informacoes.Valores;
using CTe.Servicos.Enderecos.Helpers;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using CteVersion = CTe.Classes.Servicos.Tipos.versao;
using ZeusCte = CTe.Classes.CTe;
using ZeusIcms = CTe.Classes.Informacoes.Impostos.Tributacao.ICMS;
using ZeusInfNFe = CTe.Classes.Informacoes.infCTeNormal.infDocumentos.infNFe;
using ZeusInfRespTec = CTe.Classes.Informacoes.infRespTec.infRespTec;

namespace SiagroB1.Fiscal.Cte;

/// <summary>
/// Traduz <see cref="CteIssueInput"/> para o modelo da Zeus: CT-e normal (tpCTe 0), serviço
/// normal (tpServ 0), modal rodoviário, emissão normal. A chave e o <c>cDV</c> saem de
/// <see cref="CteAccessKey"/>; a assinatura fica com <see cref="CteSigner"/>.
/// </summary>
public static class CteXmlBuilder
{
    private const string ProcessVersion = "SiagroB1";

    public static ZeusCte Build(CteIssueInput input)
    {
        var issuerState = State(input.Issuer.Address.State);
        var key = CteAccessKey.Build(
            ((int)issuerState).ToString("00", CultureInfo.InvariantCulture), input.IssuedAt.DateTime,
            input.Issuer.TaxId, input.Series, input.Number, 1, input.RandomCode);

        return new ZeusCte
        {
            infCTeSupl = new infCTeSupl { qrCodCTe = QrCode(input, issuerState, key) },
            infCte = new infCte
            {
                versao = CteVersion.ve400,
                Id = "CTe" + key,
                ide = BuildIde(input, issuerState, key),
                compl = BuildComplement(input),
                emit = BuildIssuer(input.Issuer),
                rem = BuildSender(input.Sender),
                exped = input.Shipper is null ? null : BuildShipper(input.Shipper),
                receb = input.Receiver is null ? null : BuildReceiver(input.Receiver),
                dest = BuildRecipient(input.Recipient),
                vPrest = new vPrest
                {
                    vTPrest = input.ServiceValue,
                    vRec = input.ReceivableValue,
                    Comp = input.Components.Select(c => new CTe.Classes.Informacoes.Valores.Comp { xNome = c.Name, vComp = c.Value }).ToList(),
                },
                imp = BuildTaxes(input),
                infCTeNorm = BuildNormal(input),
                autXML = input.AuthorizedXmlTaxIds.Count == 0
                    ? null
                    : input.AuthorizedXmlTaxIds.Select(id => id.Length == 11 ? new autXML { CPF = id } : new autXML { CNPJ = id }).ToList(),
                infRespTec = input.TechnicalResponsible is null
                    ? null
                    : new ZeusInfRespTec
                    {
                        CNPJ = input.TechnicalResponsible.Cnpj,
                        xContato = input.TechnicalResponsible.Contact,
                        email = input.TechnicalResponsible.Email,
                        fone = input.TechnicalResponsible.Phone,
                    },
            },
        };
    }

    /// <summary>
    /// <c>infCTeSupl/qrCodCTe</c>: o XSD aceita sem, mas a SEFAZ recusa o CT-e 4.00 sem QR Code.
    /// A URL por UF/ambiente vem da tabela da Zeus (<c>UrlHelper.ObterUrlQrCode</c>).
    /// </summary>
    private static string QrCode(CteIssueInput input, Estado issuerState, string key)
    {
        CteZeusConfiguration.PinGlobalLayout();
        var url = UrlHelper.ObterUrlQrCode(new CTe.Classes.ConfiguracaoServico
        {
            cUF = issuerState,
            tpAmb = NfeXmlBuilder.Environment(input.Environment),
            VersaoLayout = CTe.Classes.Servicos.Tipos.versao.ve400,
        });
        if (string.IsNullOrWhiteSpace(url))
            throw new DefaultException($"Sem URL do QR Code do CT-e para a UF {issuerState}.");

        var environment = input.Environment == NfeEnvironment.Production ? 1 : 2;
        return $"{url}?chCTe={key}&tpAmb={environment}";
    }

    private static ide BuildIde(CteIssueInput input, Estado issuerState, string key)
    {
        var ide = new ide
        {
            cUF = issuerState,
            cCT = int.Parse(input.RandomCode, CultureInfo.InvariantCulture),
            CFOP = int.Parse(input.Cfop, CultureInfo.InvariantCulture),
            natOp = input.OperationNature,
            mod = ModeloDocumento.CTe,
            serie = (short)input.Series,
            nCT = input.Number,
            dhEmi = input.IssuedAt,
            tpImp = tpImp.Retrado,
            tpEmis = tpEmis.teNormal,
            cDV = key[43] - '0',
            tpAmb = NfeXmlBuilder.Environment(input.Environment),
            tpCTe = tpCTe.Normal,
            procEmi = procEmi.AplicativoContribuinte,
            verProc = ProcessVersion,
            cMunEnv = long.Parse(input.Issuer.Address.MunicipalityCode, CultureInfo.InvariantCulture),
            xMunEnv = input.Issuer.Address.MunicipalityName,
            UFEnv = issuerState,
            modal = modal.rodoviario,
            tpServ = tpServ.normal,
            cMunIni = long.Parse(input.Route.StartMunicipalityCode, CultureInfo.InvariantCulture),
            xMunIni = input.Route.StartMunicipalityName,
            UFIni = State(input.Route.StartState),
            cMunFim = long.Parse(input.Route.EndMunicipalityCode, CultureInfo.InvariantCulture),
            xMunFim = input.Route.EndMunicipalityName,
            UFFim = State(input.Route.EndState),
            retira = retira.Nao,
            indIEToma = input.TakerIndicator switch
            {
                1 => indIEToma.ContribuinteIcms,
                2 => indIEToma.ContribuinteIsentoDeInscricao,
                9 => indIEToma.NaoContribuinte,
                _ => throw new DefaultException($"Indicador de IE do tomador {input.TakerIndicator} inválido (1, 2 ou 9)."),
            },
        };

        if (input.Taker == CteTaker.Other)
        {
            var other = input.OtherTaker
                ?? throw new DefaultException("O tomador \"outros\" exige os dados do tomador.");
            ide.toma4 = new toma4
            {
                toma = toma.Outros,
                CNPJ = other.TaxId.Length == 11 ? null : other.TaxId,
                CPF = other.TaxId.Length == 11 ? other.TaxId : null,
                IE = Blank(other.StateRegistration),
                xNome = other.Name,
                xFant = Blank(other.TradeName),
                fone = Blank(other.Address.Phone),
                enderToma = new enderToma
                {
                    xLgr = other.Address.Street,
                    nro = other.Address.Number,
                    xCpl = Blank(other.Address.Complement),
                    xBairro = other.Address.District,
                    cMun = long.Parse(other.Address.MunicipalityCode, CultureInfo.InvariantCulture),
                    xMun = other.Address.MunicipalityName,
                    CEP = ZipCode(other.Address) is { } cep ? cep : null,
                    UF = State(other.Address.State),
                },
                email = Blank(other.Email),
            };
        }
        else
        {
            ide.tomaBase3 = new toma3
            {
                toma = input.Taker switch
                {
                    CteTaker.Sender => toma.Remetente,
                    CteTaker.Shipper => toma.Expedidor,
                    CteTaker.Receiver => toma.Recebedor,
                    _ => toma.Destinatario,
                },
            };
        }

        return ide;
    }

    private static compl? BuildComplement(CteIssueInput input)
    {
        if (Blank(input.Remarks) is null && input.Observations.Count == 0)
            return null;

        return new compl
        {
            xObs = Blank(input.Remarks),
            ObsCont = input.Observations.Count == 0
                ? null
                : input.Observations.Select(o => new ObsCont { xCampo = o.Field, xTexto = o.Text }).ToList(),
        };
    }

    private static emit BuildIssuer(CteIssuer issuer) => new()
    {
        CNPJ = issuer.TaxId,
        IE = issuer.StateRegistration,
        xNome = issuer.LegalName,
        xFant = Blank(issuer.TradeName),
        enderEmit = new enderEmit
        {
            xLgr = issuer.Address.Street,
            nro = issuer.Address.Number,
            xCpl = Blank(issuer.Address.Complement),
            xBairro = issuer.Address.District,
            cMun = long.Parse(issuer.Address.MunicipalityCode, CultureInfo.InvariantCulture),
            xMun = issuer.Address.MunicipalityName,
            CEP = ZipCode(issuer.Address) ?? 0,
            UF = State(issuer.Address.State),
            fone = Blank(issuer.Address.Phone),
        },
        CRT = issuer.TaxRegime switch
        {
            TaxRegime.SimplesNacional => CRT.SimplesNacional,
            TaxRegime.SimplesNacionalExcess => CRT.SimplesNacionalExcessoSublimite,
            TaxRegime.Mei => CRT.SimplesNacionalMei,
            _ => CRT.RegimeNormal,
        },
    };

    private static rem BuildSender(CteParty p) => new()
    {
        CNPJ = Cnpj(p), CPF = Cpf(p), IE = Blank(p.StateRegistration), xNome = p.Name, xFant = Blank(p.TradeName),
        fone = Blank(p.Address.Phone), email = Blank(p.Email),
        enderReme = new enderReme
        {
            xLgr = p.Address.Street, nro = p.Address.Number, xCpl = Blank(p.Address.Complement), xBairro = p.Address.District,
            cMun = Municipality(p.Address), xMun = p.Address.MunicipalityName, CEP = ZipCode(p.Address) ?? 0, UF = State(p.Address.State),
        },
    };

    private static exped BuildShipper(CteParty p) => new()
    {
        CNPJ = Cnpj(p), CPF = Cpf(p), IE = Blank(p.StateRegistration), xNome = p.Name,
        fone = Blank(p.Address.Phone), email = Blank(p.Email),
        enderExped = new enderExped
        {
            xLgr = p.Address.Street, nro = p.Address.Number, xCpl = Blank(p.Address.Complement), xBairro = p.Address.District,
            cMun = Municipality(p.Address), xMun = p.Address.MunicipalityName, CEP = ZipCode(p.Address) ?? 0, UF = State(p.Address.State),
        },
    };

    private static receb BuildReceiver(CteParty p) => new()
    {
        CNPJ = Cnpj(p), CPF = Cpf(p), IE = Blank(p.StateRegistration), xNome = p.Name,
        fone = Blank(p.Address.Phone), email = Blank(p.Email),
        enderReceb = new enderReceb
        {
            xLgr = p.Address.Street, nro = p.Address.Number, xCpl = Blank(p.Address.Complement), xBairro = p.Address.District,
            cMun = Municipality(p.Address), xMun = p.Address.MunicipalityName, CEP = ZipCode(p.Address) ?? 0, UF = State(p.Address.State),
        },
    };

    private static dest BuildRecipient(CteParty p) => new()
    {
        CNPJ = Cnpj(p), CPF = Cpf(p), IE = Blank(p.StateRegistration), xNome = p.Name,
        fone = Blank(p.Address.Phone), email = Blank(p.Email),
        enderDest = new enderDest
        {
            xLgr = p.Address.Street, nro = p.Address.Number, xCpl = Blank(p.Address.Complement), xBairro = p.Address.District,
            cMun = Municipality(p.Address), xMun = p.Address.MunicipalityName, CEP = ZipCode(p.Address) ?? 0, UF = State(p.Address.State),
        },
    };

    private static imp BuildTaxes(CteIssueInput input) => new()
    {
        ICMS = new ZeusIcms { TipoICMS = BuildIcms(input) },
        IBSCBS = BuildIbsCbs(input),
    };

    private static ICMSBasico BuildIcms(CteIssueInput input) => input.IcmsCst switch
    {
        "00" => new ICMS00 { CST = CST.ICMS00, vBC = input.IcmsBase, pICMS = input.IcmsRate, vICMS = input.IcmsValue },
        "20" => new ICMS20
        {
            CST = CST.ICMS20, pRedBC = input.IcmsBaseReduction, vBC = input.IcmsBase, pICMS = input.IcmsRate, vICMS = input.IcmsValue,
        },
        "40" => new ICMS45 { CST = CST.ICMS40 },
        "41" => new ICMS45 { CST = CST.ICMS41 },
        "51" => new ICMS45 { CST = CST.ICMS51 },
        "90" => new ICMS90
        {
            CST = CST.ICMS90,
            pRedBC = input.IcmsBaseReduction > 0 ? input.IcmsBaseReduction : null,
            vBC = input.IcmsBase, pICMS = input.IcmsRate, vICMS = input.IcmsValue,
        },
        _ => throw new DefaultException($"O CST de ICMS {input.IcmsCst} não é emitido por este sistema no CT-e."),
    };

    private static IBSCBS? BuildIbsCbs(CteIssueInput input)
    {
        if (input.IbsCbsCst is null)
            return null;

        var group = new IBSCBS
        {
            CST = Enum.Parse<CSTIBSCBS>("cst" + input.IbsCbsCst),
            cClassTrib = input.IbsCbsClassCode,
        };

        if (input.IbsCbsBase <= 0)
            return group;

        group.gIBSCBS = new gIBSCBS
        {
            vBC = input.IbsCbsBase,
            gIBSUF = new gIBSUF { pIBSUF = input.IbsStateRate, vIBSUF = input.IbsStateValue },
            gIBSMun = new gIBSMun { pIBSMun = input.IbsMunicipalRate, vIBSMun = input.IbsMunicipalValue },
            vIBS = input.IbsStateValue + input.IbsMunicipalValue,
            gCBS = new gCBS { pCBS = input.CbsRate, vCBS = input.CbsValue },
        };

        return group;
    }

    private static infCTeNorm BuildNormal(CteIssueInput input) => new()
    {
        infCarga = new infCarga
        {
            vCarga = input.CargoValue,
            proPred = input.PredominantProduct,
            infQ = input.CargoQuantities.Select(q => new infQ
            {
                cUnid = (cUnid)int.Parse(q.UnitCode, CultureInfo.InvariantCulture),
                tpMed = q.MeasureType,
                qCarga = q.Quantity,
            }).ToList(),
        },
        infDoc = new infDoc
        {
            infNFe = input.NfeKeys.Select(k => new ZeusInfNFe { chave = k }).ToList(),
        },
        infModal = new infModal
        {
            versaoModal = versaoModal.veM400,
            ContainerModal = new rodo { RNTRC = input.Issuer.Rntrc },
        },
    };

    private static Estado State(string uf) => Enum.Parse<Estado>(uf, ignoreCase: true);

    private static long Municipality(NfeAddress a) => long.Parse(a.MunicipalityCode, CultureInfo.InvariantCulture);

    private static long? ZipCode(NfeAddress a) =>
        string.IsNullOrWhiteSpace(a.ZipCode) ? null : long.Parse(a.ZipCode, CultureInfo.InvariantCulture);

    private static string? Cnpj(CteParty p) => p.TaxId.Length == 11 ? null : p.TaxId;

    private static string? Cpf(CteParty p) => p.TaxId.Length == 11 ? p.TaxId : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
