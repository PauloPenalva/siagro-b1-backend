// Spike da API Zeus.Net.MDFe 2026.9.24.1416 (mesma versão da Zeus CT-e/NF-e; DFe.* idênticos).
//
// Tipos usados (todos verificados por estes testes):
// - Documento: MDFe.Classes.Informacoes.MDFe { InfMDFe, InfMDFeSupl, Signature }
//   MDFeInfMDFe { Versao (MDFe.Utils.Flags.VersaoServico), Id, Ide, Emit, InfModal, InfDoc, Seg, ProdPred, Tot, ... }
//   MDFeIde(VersaoServico?) — passar Versao300; MDFeEmit/MDFeEnderEmit; MDFeInfMunCarrega; MDFeInfPercurso
//   InfModal = MDFeInfModal { VersaoModal = MDFeVersaoModal.Versao300, Modal = MDFeRodo }
//   MDFeRodo { InfANTT, VeicTracao (MDFeVeicTracao { Condutor: List<MDFeCondutor> }), VeicReboque, LacRodo }
//   MDFeInfANTT { RNTRC, InfCIOT: List<infCIOT { CIOT, CPF, CNPJ }>, ValePed, InfContratante, InfPag: List<MDFeInfPag> }
//   MDFeInfPag { XNome, CPF/CNPJ, Comp: List<MDFeComp>, VContratoProxy, IndPag, InfPrazo, InfBanc, IndAltoDesemp }
//   MDFeInfDoc { InfMunDescarga: List<MDFeInfMunDescarga { CMunDescarga, XMunDescarga, InfCTe, InfNFe }> }
//   Flags em MDFe.Classes.Flags; Estado/TipoAmbiente/ModeloDocumento em DFe.Classes.*.
// - XML: ExtMDFe.XmlString(), ExtMDFe.Valida(cfg) (XSD em cfg.CaminhoSchemas), ExtMDFe.Chave(),
//   MDFe.LoadXmlString(xml). Chave: DFe.Utils.ChaveFiscal.ObterChave(...).
// - Assinatura: DFe.Utils.Assinatura.AssinaturaDigital.Assina(obj, id, X509Certificate2) — não ExtMDFe.Assina,
//   que relê o certificado de cfg.ConfiguracaoCertificado.
// - Serviços (MDFe.Servicos.*, todos pegam MDFeConfiguracao e usam cfg.X509Certificate2 — evitar):
//   ServicoMDFeRecepcao.MDFeRecepcaoSinc(MDFe, cfg) : MDFeRetMDFe; ServicoMDFeConsultaProtocolo.MDFeConsultaProtocolo(chave, cfg);
//   EventoEncerramento/EventoCancelar/EventoInclusaoCondutor; ServicoMDFeConsultaNaoEncerrados.MDFeConsultaNaoEncerrados(cnpjCpf, cfg).
//   Nível WSDL (aceita o certificado aberto via DFe.Wsdl.WsdlConfiguracao): MDFe.Wsdl.MDFeRecepcao.Sincrono.MDFeRecepcaoSinc.mdfeRecepcao,
//   MDFe.Wsdl.MDFeConsultaProtoloco.MDFeConsulta.mdfeConsultaMDF, MDFe.Wsdl.MDFeEventos.MDFeRecepcaoEvento.mdfeRecepcaoEvento,
//   MDFe.Wsdl.MDFeConsultaNaoEncerrados.MDFeConsNaoEnc.mdfeConsNaoEnc, MDFe.Wsdl.Gerado.MDFeStatusServico.MDFeStatusServico.
//   Pedidos: MDFe.Servicos.Factory.ClassesFactory.CriarConsSitMDFe/CriarConsMDFeNaoEnc/CriaEvEncMDFe/CriaEvCancMDFe/
//   CriaEvIncCondutorMDFe; MDFe.Servicos.EventosMDFe.FactoryEvento.CriaEvento(mdfe, MDFeTipoEvento, seq, container, cfg).
// - Configuração global: MDFe.Utils.Configuracoes.MDFeConfiguracao.Instancia (singleton próprio, ≠ do CT-e).
using System.Security.Cryptography.X509Certificates;
using CTe.Classes;
using CTe.Classes.Servicos.Tipos;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using DFe.Utils;
using DFe.Utils.Assinatura;
using MDFe.Classes.Extensoes;
using MDFe.Classes.Flags;
using MDFe.Classes.Informacoes;
using MDFe.Utils.Configuracoes;
using VersaoServico = MDFe.Utils.Flags.VersaoServico;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Tests.Support;
using ZeusMdfe = MDFe.Classes.Informacoes.MDFe;

namespace SiagroB1.Fiscal.Tests.Mdfe;

public class ZeusMdfeApiSpikeTests
{
    private static ZeusMdfe Rodoviario()
    {
        var mdfe = new ZeusMdfe
        {
            InfMDFe = new MDFeInfMDFe
            {
                Versao = VersaoServico.Versao300,
                Ide = new MDFeIde(VersaoServico.Versao300)
                {
                    CUF = Estado.SP,
                    TpAmb = TipoAmbiente.Homologacao,
                    TpEmit = MDFeTipoEmitente.TransportadorCargaPropria,
                    Mod = ModeloDocumento.MDFe,
                    Serie = 1,
                    NMDF = 1,
                    CMDF = 12345678,
                    Modal = MDFeModal.Rodoviario,
                    DhEmi = new DateTime(2026, 10, 23, 10, 0, 0),
                    TpEmis = MDFeTipoEmissao.Normal,
                    ProcEmi = MDFeIdentificacaoProcessoEmissao.EmissaoComAplicativoContribuinte,
                    VerProc = "SiagroB1",
                    UFIni = Estado.SP,
                    UFFim = Estado.SP,
                    InfMunCarrega = [new MDFeInfMunCarrega { CMunCarrega = "3550308", XMunCarrega = "SAO PAULO" }],
                },
                Emit = new MDFeEmit
                {
                    CNPJ = "11222333000181",
                    IE = "111111111111",
                    XNome = "EMITENTE TESTE",
                    EnderEmit = new MDFeEnderEmit
                    {
                        XLgr = "RUA A", Nro = "1", XBairro = "CENTRO", CMun = 3550308, XMun = "SAO PAULO",
                        CEP = 1001000, UF = Estado.SP,
                    },
                },
                InfModal = new MDFeInfModal
                {
                    VersaoModal = MDFeVersaoModal.Versao300,
                    Modal = new MDFeRodo
                    {
                        InfANTT = new MDFeInfANTT
                        {
                            RNTRC = "12345678",
                            InfCIOT = [new infCIOT { CIOT = "123456789012", CNPJ = "11222333000181" }],
                            InfPag =
                            [
                                new MDFeInfPag
                                {
                                    XNome = "CONTRATANTE", CNPJ = "11222333000181",
                                    Comp = [new MDFeComp { TpComp = MDFeTpComp.Outros, VComp = 1000m, XComp = "FRETE" }],
                                    VContratoProxy = 1000m,
                                    IndPag = MDFeIndPag.PagamentoVista,
                                    InfBanc = new MDFeInfBanc { PIX = "11222333000181" },
                                },
                            ],
                        },
                        VeicTracao = new MDFeVeicTracao
                        {
                            CInt = "1", Placa = "ABC1D23", Tara = 8000,
                            Condutor = [new MDFeCondutor { XNome = "MOTORISTA", CPF = "12345678909" }],
                            TpRod = MDFeTpRod.Truck, TpCar = MDFeTpCar.Granelera, UF = Estado.SP,
                        },
                    },
                },
                InfDoc = new MDFeInfDoc
                {
                    InfMunDescarga =
                    [
                        new MDFeInfMunDescarga
                        {
                            CMunDescarga = "3550308", XMunDescarga = "SAO PAULO",
                            InfNFe = [new MDFeInfNFe { ChNFe = NfeTestData.SaleAccessKey }],
                        },
                    ],
                },
                Tot = new MDFeTot { QNFe = 1, vCarga = 50000m, CUnid = MDFeCUnid.KG, QCarga = 30000m },
            },
        };

        var key = ChaveFiscal.ObterChave(Estado.SP, mdfe.InfMDFe.Ide.DhEmi, "11222333000181", ModeloDocumento.MDFe, 1, 1,
            (int)MDFeTipoEmissao.Normal, 12345678);
        mdfe.InfMDFe.Id = "MDFe" + key.Chave;
        mdfe.InfMDFe.Ide.CDV = key.DigitoVerificador;
        mdfe.InfMDFeSupl = new MdfeInfMDFeSupl { QrCodMDFe = MdfeInfMDFeSupl.GerarQrCode(key.Chave, TipoAmbiente.Homologacao) };
        return mdfe;
    }

    [Fact]
    public void Mdfe_types_used_by_builder_exist()
    {
        MdfeZeusConfiguration.PinGlobalLayout();

        var xml = Rodoviario().XmlString();

        Assert.Contains("<infANTT><RNTRC>12345678</RNTRC><infCIOT><CIOT>123456789012</CIOT>", xml);
        Assert.Contains("<infPag>", xml);
        Assert.Contains("versao=\"3.00\"", xml);
        Assert.Contains("<dhEmi>2026-10-23T10:00:00", xml);
    }

    [Fact]
    public void Signed_mdfe_validates_against_xsd_and_round_trips()
    {
        MdfeZeusConfiguration.PinGlobalLayout();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new MdfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate,
            MdfeZeusConfiguration.DefaultSchemasDirectory);
        var mdfe = Rodoviario();

        mdfe.Signature = AssinaturaDigital.Assina(mdfe, mdfe.InfMDFe.Id, certificate);
        mdfe.Valida(MdfeZeusConfiguration.Create(settings));

        var xml = mdfe.XmlString();
        Assert.Contains("<Signature", xml);
        Assert.Equal(mdfe.InfMDFe.Id[4..], ZeusMdfe.LoadXmlString(xml).Chave());
    }

    [Fact]
    public void Create_maps_settings_to_a_per_call_configuration()
    {
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var configuration = MdfeZeusConfiguration.Create(
            new MdfeServiceSettings(NfeEnvironment.Homologation, "pr", certificate, MdfeZeusConfiguration.DefaultSchemasDirectory, 30_000));

        Assert.NotSame(MDFeConfiguracao.Instancia, configuration);
        Assert.Equal(TipoAmbiente.Homologacao, configuration.VersaoWebService.TipoAmbiente);
        Assert.Equal(Estado.PR, configuration.VersaoWebService.UfEmitente);
        Assert.Equal(VersaoServico.Versao300, configuration.VersaoWebService.VersaoLayout);
        Assert.Equal(30_000, configuration.VersaoWebService.TimeOut);
        Assert.Equal(MdfeZeusConfiguration.DefaultSchemasDirectory, configuration.CaminhoSchemas);
    }

    [Fact]
    public void Cte_and_mdfe_global_configs_coexist()
    {
        // Singletons distintos: fixar um não altera o outro.
        Assert.NotEqual(typeof(ConfiguracaoServico).Assembly, typeof(MDFeConfiguracao).Assembly);

        CteZeusConfiguration.PinGlobalLayout();
        Assert.Equal(versao.ve400, ConfiguracaoServico.Instancia.VersaoLayout);

        MdfeZeusConfiguration.PinGlobalLayout();
        Assert.Equal(VersaoServico.Versao300, MDFeConfiguracao.Instancia.VersaoWebService.VersaoLayout);
        Assert.Equal(versao.ve400, ConfiguracaoServico.Instancia.VersaoLayout);

        CteZeusConfiguration.PinGlobalLayout();
        Assert.Equal(versao.ve400, ConfiguracaoServico.Instancia.VersaoLayout);
        Assert.Equal(VersaoServico.Versao300, MDFeConfiguracao.Instancia.VersaoWebService.VersaoLayout);
    }

    [Fact]
    public void Unpinned_global_layout_breaks_dhEmi_serialization()
    {
        // Documenta o porquê do PinGlobalLayout: o padrão do global não é versão válida.
        Assert.False(Enum.IsDefined(new MDFeVersaoWebService().VersaoLayout));
    }

    [Fact]
    public void Mdfe_xsd_directory_is_copied()
    {
        Assert.True(File.Exists(Path.Combine(MdfeZeusConfiguration.DefaultSchemasDirectory, "mdfe_v3.00.xsd")));
        Assert.True(File.Exists(Path.Combine(MdfeZeusConfiguration.DefaultSchemasDirectory, "mdfeModalRodoviario_v3.00.xsd")));
        Assert.True(File.Exists(Path.Combine(MdfeZeusConfiguration.DefaultSchemasDirectory, "xmldsig-core-schema_v1.01.xsd")));
    }
}
