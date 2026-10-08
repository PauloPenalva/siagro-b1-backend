using NFe.Utils.NFe;
using NFe.Classes.Informacoes.Identificacao;
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Transporte;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>Tradução da entrada para o modelo da Zeus — spec §8, grupo a grupo.</summary>
public class NfeXmlBuilderTests
{
    private static NFe.Classes.NFe Build(NfeIssueInput input) => NfeXmlBuilder.Build(input);

    [Fact]
    public void Homologation_replaces_the_recipient_name_with_the_official_literal()
    {
        var nfe = Build(NfeTestData.Input(NfeEnvironment.Homologation));

        Assert.Equal("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", nfe.infNFe.dest.xNome);
        Assert.Equal(TipoAmbiente.Homologacao, nfe.infNFe.ide.tpAmb);
    }

    [Fact]
    public void Production_keeps_the_recipient_name()
    {
        var nfe = Build(NfeTestData.Input(NfeEnvironment.Production));

        Assert.Equal("CLIENTE BA LTDA", nfe.infNFe.dest.xNome);
        Assert.Equal(TipoAmbiente.Producao, nfe.infNFe.ide.tpAmb);
    }

    [Fact]
    public void Ide_comes_from_the_input_and_the_issuer_municipality()
    {
        var ide = Build(NfeTestData.Input()).infNFe.ide;

        Assert.Equal(Estado.SP, ide.cUF);
        Assert.Equal(3521705L, ide.cMunFG);
        Assert.Equal("48151623", ide.cNF);
        Assert.Equal(1, ide.serie);
        Assert.Equal(123L, ide.nNF);
        Assert.Equal(ModeloDocumento.NFe, ide.mod);
        Assert.Equal(NfeTestData.IssuedAt, ide.dhEmi);
        Assert.Equal(TipoNFe.tnSaida, ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnNormal, ide.finNFe);
        Assert.Equal(PresencaComprador.pcOutros, ide.indPres);
        Assert.Equal(IndicadorIntermediador.iiSemIntermediador, ide.indIntermed);
        Assert.Equal("VENDA DE PRODUCAO DO ESTABELECIMENTO", ide.natOp);
    }

    [Fact]
    public void Operation_nature_is_cut_at_60_characters()
    {
        var input = NfeTestData.Input() with { OperationNature = new string('X', 80) };

        Assert.Equal(60, Build(input).infNFe.ide.natOp.Length);
    }

    [Fact]
    public void Interstate_recipient_sets_idDest_2_and_omits_the_vehicle()
    {
        var nfe = Build(NfeTestData.Input(recipientAddress: NfeTestData.Salvador()));

        Assert.Equal(DestinoOperacao.doInterestadual, nfe.infNFe.ide.idDest);
        Assert.Null(nfe.infNFe.transp.veicTransp);
    }

    [Fact]
    public void In_state_recipient_sets_idDest_1_and_sends_the_vehicle()
    {
        var nfe = Build(NfeTestData.Input(recipientAddress: NfeTestData.SaoPaulo()));

        Assert.Equal(DestinoOperacao.doInterna, nfe.infNFe.ide.idDest);
        Assert.Equal("ABC1D23", nfe.infNFe.transp.veicTransp.placa);
    }

    [Fact]
    public void Non_taxpayer_is_final_consumer_without_state_registration()
    {
        var input = NfeTestData.Input();
        input = input with
        {
            Recipient = input.Recipient with
            {
                TaxId = "12345678909", Indicator = StateRegistrationIndicator.NonTaxpayer, StateRegistration = "123",
            },
        };

        var nfe = Build(input);

        Assert.Equal(ConsumidorFinal.cfConsumidorFinal, nfe.infNFe.ide.indFinal);
        Assert.Equal(indIEDest.NaoContribuinte, nfe.infNFe.dest.indIEDest);
        Assert.Null(nfe.infNFe.dest.IE);
        Assert.Equal("12345678909", nfe.infNFe.dest.CPF);
        Assert.Null(nfe.infNFe.dest.CNPJ);
    }

    [Fact]
    public void Taxpayer_sends_the_state_registration()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Equal(ConsumidorFinal.cfNao, nfe.infNFe.ide.indFinal);
        Assert.Equal(indIEDest.ContribuinteICMS, nfe.infNFe.dest.indIEDest);
        Assert.Equal("123456789", nfe.infNFe.dest.IE);
        Assert.Equal("11222333000181", nfe.infNFe.dest.CNPJ);
    }

    [Theory]
    [InlineData("00", typeof(ICMS00))]
    [InlineData("20", typeof(ICMS20))]
    [InlineData("40", typeof(ICMS40))]
    [InlineData("41", typeof(ICMS40))]
    [InlineData("50", typeof(ICMS40))]
    [InlineData("51", typeof(ICMS51))]
    [InlineData("90", typeof(ICMS90))]
    [InlineData("102", typeof(ICMSSN102))]
    [InlineData("300", typeof(ICMSSN102))]
    [InlineData("900", typeof(ICMSSN900))]
    public void Icms_group_follows_the_recorded_code(string code, Type expected)
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { IcmsCode = code }] };

        Assert.IsType(expected, Build(input).infNFe.det[0].imposto.ICMS.TipoICMS);
    }

    [Fact]
    public void Icms51_carries_the_deferral()
    {
        var item = NfeTestData.Item() with
        {
            IcmsCode = "51", IcmsBase = 60000m, IcmsRate = 18m, IcmsOperationValue = 10800m, IcmsDeferral = 100m,
            IcmsDeferredValue = 10800m, IcmsValue = 0m,
        };

        var icms = Assert.IsType<ICMS51>(Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].imposto.ICMS.TipoICMS);

        Assert.Equal(10800m, icms.vICMSOp);
        Assert.Equal(100m, icms.pDif);
        Assert.Equal(10800m, icms.vICMSDif);
        Assert.Equal(0m, icms.vICMS);
        Assert.Equal(0m, icms.pRedBC);
    }

    /// <summary>
    /// Regra N12-97 (rejeição 929): no CST 51 a SEFAZ-SP exige o grupo inteiro, inclusive o pRedBC
    /// sem redução — em 08/10/2026 passou a rejeitar o XML que antes autorizava sem a tag.
    /// </summary>
    [Fact]
    public void Icms51_without_reduction_serializes_pRedBC_zero()
    {
        var item = NfeTestData.Item() with
        {
            IcmsCode = "51", IcmsBase = 66429m, IcmsRate = 18m, IcmsOperationValue = 11957.22m, IcmsDeferral = 100m,
            IcmsDeferredValue = 11957.22m, IcmsValue = 0m, IcmsBaseReduction = 0m,
        };

        var xml = Build(NfeTestData.Input() with { Items = [item] }).ObterXmlString();

        Assert.Contains("<modBC>3</modBC><pRedBC>0.0000</pRedBC><vBC>66429.00</vBC>", xml);
    }

    [Theory]
    [InlineData("01", typeof(PISAliq))]
    [InlineData("02", typeof(PISAliq))]
    [InlineData("06", typeof(PISNT))]
    [InlineData("09", typeof(PISNT))]
    [InlineData("49", typeof(PISOutr))]
    public void Pis_group_follows_the_cst(string cst, Type expected)
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { PisCst = cst, CofinsCst = cst }] };
        var imposto = Build(input).infNFe.det[0].imposto;

        Assert.IsType(expected, imposto.PIS.TipoPIS);
        Assert.Equal(expected.Name.Replace("PIS", "COFINS"), imposto.COFINS.TipoCOFINS.GetType().Name);
    }

    [Fact]
    public void Ibs_cbs_group_carries_rates_and_values()
    {
        var group = Build(NfeTestData.Input()).infNFe.det[0].imposto.IBSCBS;

        Assert.Equal("000001", group.cClassTrib);
        Assert.Equal(50638.50m, group.gIBSCBS.vBC);
        Assert.Equal(0.1m, group.gIBSCBS.gIBSUF.pIBSUF);
        Assert.Equal(50.64m, group.gIBSCBS.gIBSUF.vIBSUF);
        Assert.Equal(50.64m, group.gIBSCBS.vIBS);
        Assert.Equal(455.75m, group.gIBSCBS.gCBS.vCBS);
        Assert.Null(group.gIBSCBS.gCBS.gRed);
    }

    [Fact]
    public void Ibs_cbs_reduction_fills_the_effective_rate()
    {
        var item = NfeTestData.Item() with { IbsCbsCst = "200", CbsRateReduction = 60m, IbsRateReduction = 60m };

        var group = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].imposto.IBSCBS;

        Assert.Equal(60m, group.gIBSCBS.gCBS.gRed.pRedAliq);
        Assert.Equal(0.36m, group.gIBSCBS.gCBS.gRed.pAliqEfet);
        Assert.Equal(0.04m, group.gIBSCBS.gIBSUF.gRed.pAliqEfet);
    }

    [Fact]
    public void Ibs_cbs_without_values_has_no_detail_group()
    {
        var item = NfeTestData.Item() with { IbsCbsCst = "410" };

        var group = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].imposto.IBSCBS;

        Assert.Null(group.gIBSCBS);
    }

    [Fact]
    public void Line_without_ibs_cbs_has_no_group_and_no_total()
    {
        var item = NfeTestData.Item() with { IbsCbsCst = null, IbsCbsClassCode = null };

        var nfe = Build(NfeTestData.Input() with { Items = [item] });

        Assert.Null(nfe.infNFe.det[0].imposto.IBSCBS);
        Assert.Null(nfe.infNFe.total.IBSCBSTot);
    }

    [Fact]
    public void Totals_add_up_the_lines()
    {
        var input = NfeTestData.Input() with
        {
            Items = [NfeTestData.Item(1), NfeTestData.Item(2) with { IcmsCode = "41" }],
            Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", 120000m,
                new DateOnly(2026, 10, 2)),
        };

        var total = Build(input).infNFe.total;

        Assert.Equal(120000m, total.ICMSTot.vProd);
        Assert.Equal(120000m, total.ICMSTot.vNF);
        Assert.Equal(60000m, total.ICMSTot.vBC);      // o item 41 não leva base
        Assert.Equal(4200m, total.ICMSTot.vICMS);
        Assert.Equal(1841.40m, total.ICMSTot.vPIS);
        Assert.Equal(8481.60m, total.ICMSTot.vCOFINS);
        Assert.Equal(101277.00m, total.IBSCBSTot.vBCIBSCBS);
        Assert.Equal(911.50m, total.IBSCBSTot.gCBS.vCBS);
        Assert.Equal(101.28m, total.IBSCBSTot.gIBS.vIBS);
    }

    [Theory]
    [InlineData(FreightTerms.Cif, ModalidadeFrete.mfContaEmitenteOumfContaRemetente)]
    [InlineData(FreightTerms.Fob, ModalidadeFrete.mfContaDestinatario)]
    [InlineData(FreightTerms.Ter, ModalidadeFrete.mfContaTerceiros)]
    [InlineData(FreightTerms.None, ModalidadeFrete.mfSemFrete)]
    public void Freight_terms_map_to_modFrete(FreightTerms terms, ModalidadeFrete expected)
    {
        Assert.Equal(expected, Build(NfeTestData.Input() with { FreightTerms = terms }).infNFe.transp.modFrete);
    }

    [Fact]
    public void Carrier_and_weights_go_to_transp()
    {
        var transp = Build(NfeTestData.Input()).infNFe.transp;

        Assert.Equal("33444555000122", transp.transporta.CNPJ);
        Assert.Equal("TRANSPORTADORA TESTE LTDA", transp.transporta.xNome);
        Assert.Equal(30000m, transp.vol.Single().pesoL);
        Assert.Equal(30500m, transp.vol.Single().pesoB);
    }

    [Fact]
    public void Billing_and_payment_follow_the_plan()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Equal("000000123", nfe.infNFe.cobr.fat.nFat);
        Assert.Equal(60000m, nfe.infNFe.cobr.fat.vLiq);
        Assert.Equal(["001", "002"], nfe.infNFe.cobr.dup.Select(d => d.nDup));
        Assert.Equal([30000m, 30000m], nfe.infNFe.cobr.dup.Select(d => d.vDup));

        var detPag = nfe.infNFe.pag.Single().detPag.Single();
        Assert.Equal(IndicadorPagamentoDetalhePagamento.ipDetPgPrazo, detPag.indPag);
        Assert.Equal(15, (int)detPag.tPag);
        Assert.Equal(60000m, detPag.vPag);
    }

    [Fact]
    public void Cash_payment_omits_billing_but_keeps_the_payment_detail()
    {
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "99", 60000m, DateOnly.FromDateTime(NfeTestData.IssuedAt.Date)),
        };

        var nfe = Build(input);
        var detPag = nfe.infNFe.pag.Single().detPag.Single();

        // Rejeição 853: pagamento à vista não leva cobrança.
        Assert.Null(nfe.infNFe.cobr);
        Assert.Equal(IndicadorPagamentoDetalhePagamento.ipDetPgVista, detPag.indPag);
        Assert.Equal(60000m, detPag.vPag);
        Assert.Equal("Outros", detPag.xPag);
    }

    [Fact]
    public void No_payment_means_omits_billing_and_pays_zero()
    {
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "90", 60000m, new DateOnly(2026, 10, 2)),
        };

        var nfe = Build(input);
        var detPag = nfe.infNFe.pag.Single().detPag.Single();

        Assert.Null(nfe.infNFe.cobr);
        Assert.Equal(FormaPagamento.fpSemPagamento, detPag.tPag);
        Assert.Equal(0m, detPag.vPag);
        Assert.Null(detPag.indPag);
    }

    [Fact]
    public void Other_payment_means_describes_itself()
    {
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "99", 60000m, new DateOnly(2026, 10, 2)),
        };

        Assert.Equal("Outros", Build(input).infNFe.pag.Single().detPag.Single().xPag);
    }

    [Fact]
    public void Delivery_group_only_when_informed()
    {
        Assert.Null(Build(NfeTestData.Input()).infNFe.entrega);

        var input = NfeTestData.Input() with
        {
            Delivery = new NfeDelivery { TaxId = "44555666000177", Name = "ARMAZEM DESTINO", Address = NfeTestData.SaoPaulo() },
        };

        var entrega = Build(input).infNFe.entrega;
        Assert.Equal("44555666000177", entrega.CNPJ);
        Assert.Equal(3550308L, entrega.cMun);
        Assert.Equal("SP", entrega.UF);
    }

    [Fact]
    public void Texts_and_technical_responsible_are_sent()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Contains("Simples Nacional", nfe.infNFe.infAdic.infCpl);
        Assert.Equal("Pedido 77", nfe.infNFe.infAdic.infAdFisco);
        Assert.Equal("09123456000100", nfe.infNFe.infRespTec.CNPJ);
    }

    [Fact]
    public void Issuer_uses_crt_and_municipality()
    {
        var emit = Build(NfeTestData.Input()).infNFe.emit;

        Assert.Equal("12345678000195", emit.CNPJ);
        Assert.Equal(NFe.Classes.Informacoes.Emitente.CRT.RegimeNormal, emit.CRT);
        Assert.Equal(Estado.SP, emit.enderEmit.UF);
        Assert.Equal(3521705L, emit.enderEmit.cMun);
        Assert.Equal(1535621234L, emit.enderEmit.fone);
    }

    [Fact]
    public void Payment_that_does_not_match_the_note_total_is_refused()
    {
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", 50000m,
                new DateOnly(2026, 10, 2)),
        };

        Assert.Throws<SiagroB1.Domain.Exceptions.DefaultException>(() => Build(input));
    }

    [Fact]
    public void Pis_cst_03_is_refused()
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { PisCst = "03", CofinsCst = "03" }] };

        Assert.Throws<SiagroB1.Domain.Exceptions.DefaultException>(() => Build(input));
    }

    [Fact]
    public void Incoming_cst_goes_to_the_outr_group()
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { PisCst = "50", CofinsCst = "50" }] };
        var imposto = Build(input).infNFe.det[0].imposto;

        Assert.IsType<PISOutr>(imposto.PIS.TipoPIS);
        Assert.IsType<COFINSOutr>(imposto.COFINS.TipoCOFINS);
    }

    [Fact]
    public void Long_address_fields_are_cut_at_60_characters()
    {
        var address = NfeTestData.Salvador() with { Street = new string('A', 59) + " " + new string('B', 20) };
        var nfe = Build(NfeTestData.Input(recipientAddress: address));

        Assert.Equal(new string('A', 59), nfe.infNFe.dest.enderDest.xLgr);
    }

    [Fact]
    public void Taxpayer_recipient_without_state_registration_is_refused()
    {
        var input = NfeTestData.Input();
        input = input with { Recipient = input.Recipient with { StateRegistration = " " } };

        Assert.Throws<SiagroB1.Domain.Exceptions.DefaultException>(() => Build(input));
    }

    [Fact]
    public void No_freight_omits_carrier_and_vehicle()
    {
        var nfe = Build(NfeTestData.Input(recipientAddress: NfeTestData.SaoPaulo()) with { FreightTerms = FreightTerms.None });

        Assert.Null(nfe.infNFe.transp.transporta);
        Assert.Null(nfe.infNFe.transp.veicTransp);
        Assert.NotEmpty(nfe.infNFe.transp.vol);
    }

    [Fact]
    public void Item_with_cest_sends_cest_and_relevant_scale()
    {
        var item = NfeTestData.Item() with { Cest = "0600500" };

        var prod = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].prod;

        Assert.Equal("0600500", prod.CEST);
        Assert.Equal(indEscala.S, prod.indEscala);
    }

    [Fact]
    public void Item_without_cest_sends_neither_cest_nor_scale()
    {
        var prod = Build(NfeTestData.Input()).infNFe.det[0].prod;

        Assert.Null(prod.CEST);
        Assert.Null(prod.indEscala);
    }

    [Fact]
    public void Item_with_customer_order_sends_xped_and_nitemped()
    {
        var item = NfeTestData.Item() with { OrderNumber = "PO-77", OrderItem = "1" };

        var prod = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].prod;

        Assert.Equal("PO-77", prod.xPed);
        Assert.Equal("1", prod.nItemPed.ToString());
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("1234567")]
    public void Invalid_order_item_omits_nitemped_without_failing(string value)
    {
        var item = NfeTestData.Item() with { OrderNumber = "PO-77", OrderItem = value };

        var prod = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].prod;

        Assert.Null(prod.nItemPed);
        Assert.Equal("PO-77", prod.xPed);
    }

    [Fact]
    public void Item_without_customer_order_omits_xped_and_nitemped()
    {
        var prod = Build(NfeTestData.Input()).infNFe.det[0].prod;

        Assert.Null(prod.xPed);
        Assert.Null(prod.nItemPed);
    }

    [Fact]
    public void Volume_data_go_to_the_vol_group_with_the_weights()
    {
        var input = NfeTestData.Input() with { Volume = new NfeVolume(40, "SACO", "CEAGUI", "1 A 40") };

        var vol = Build(input).infNFe.transp.vol.Single();

        Assert.Equal(40, vol.qVol);
        Assert.Equal("SACO", vol.esp);
        Assert.Equal("CEAGUI", vol.marca);
        Assert.Equal("1 A 40", vol.nVol);
        Assert.Equal(30000m, vol.pesoL);
        Assert.Equal(30500m, vol.pesoB);
    }

    [Fact]
    public void Volume_without_the_optional_data_sends_only_the_weights()
    {
        var vol = Build(NfeTestData.Input() with { Volume = new NfeVolume(null, " ", null, "") }).infNFe.transp.vol.Single();

        Assert.Null(vol.qVol);
        Assert.Null(vol.esp);
        Assert.Null(vol.marca);
        Assert.Null(vol.nVol);
        Assert.Equal(30000m, vol.pesoL);
        Assert.Equal(30500m, vol.pesoB);
    }

    [Fact]
    public void Volume_texts_are_cut_at_60_characters()
    {
        var text = new string('S', 70);
        var vol = Build(NfeTestData.Input() with { Volume = new NfeVolume(1, text, text, text) }).infNFe.transp.vol.Single();

        Assert.Equal(60, vol.esp.Length);
        Assert.Equal(60, vol.marca.Length);
        Assert.Equal(60, vol.nVol.Length);
    }

    [Fact]
    public void Return_note_is_an_incoming_return_referenced_only_at_item_level()
    {
        var nfe = Build(NfeTestData.ReturnInput());

        Assert.Equal(TipoNFe.tnEntrada, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnDevolucao, nfe.infNFe.ide.finNFe);
        // Rejeição 1010: a referência vai num nível só; com DFeReferenciado no item, sem NFref no cabeçalho.
        Assert.Null(nfe.infNFe.ide.NFref);
        var reference = Assert.Single(nfe.infNFe.det).DFeReferenciado;
        Assert.Equal(NfeTestData.SaleAccessKey, reference.chaveAcesso);
        Assert.Equal(1, reference.nItem);
    }

    [Fact]
    public void Referenced_keys_without_item_references_still_emit_the_header_NFref()
    {
        var nfe = Build(NfeTestData.Input() with { ReferencedKeys = [NfeTestData.SaleAccessKey] });

        Assert.Equal(NfeTestData.SaleAccessKey, Assert.Single(nfe.infNFe.ide.NFref).refNFe);
        Assert.Null(Assert.Single(nfe.infNFe.det).DFeReferenciado);
    }

    [Fact]
    public void Each_returned_item_references_the_sale_item()
    {
        var det = Assert.Single(Build(NfeTestData.ReturnInput()).infNFe.det);

        Assert.Equal(NfeTestData.SaleAccessKey, det.DFeReferenciado.chaveAcesso);
        Assert.Equal(1, det.DFeReferenciado.nItem);
        Assert.Equal(1202, det.prod.CFOP);
    }

    [Fact]
    public void Return_note_pays_nothing_and_has_no_billing()
    {
        var nfe = Build(NfeTestData.ReturnInput());

        var payment = Assert.Single(Assert.Single(nfe.infNFe.pag).detPag);
        Assert.Equal(90, (int)payment.tPag!);
        Assert.Equal(0m, payment.vPag);
        Assert.Null(nfe.infNFe.cobr);
    }

    [Fact]
    public void Sale_stays_an_outgoing_normal_note_without_references()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Equal(TipoNFe.tnSaida, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnNormal, nfe.infNFe.ide.finNFe);
        Assert.Null(nfe.infNFe.ide.NFref);
        Assert.Null(Assert.Single(nfe.infNFe.det).DFeReferenciado);
    }

    [Fact]
    public void Purchase_entry_is_an_incoming_normal_note_with_the_producer_note_referenced()
    {
        var nfe = Build(NfeTestData.PurchaseEntryInput());

        Assert.Equal(TipoNFe.tnEntrada, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnNormal, nfe.infNFe.ide.finNFe);
        Assert.Equal(NfeTestData.ProducerAccessKey, Assert.Single(nfe.infNFe.ide.NFref).refNFe);
        var det = Assert.Single(nfe.infNFe.det);
        Assert.Null(det.DFeReferenciado);
        Assert.Equal(1102, det.prod.CFOP);
    }

    [Fact]
    public void Purchase_entry_from_a_non_taxpayer_is_not_a_final_consumer_operation()
    {
        Assert.Equal(ConsumidorFinal.cfNao, Build(NfeTestData.PurchaseEntryInput()).infNFe.ide.indFinal);
    }

    [Fact]
    public void Purchase_entry_bills_by_the_payment_plan()
    {
        var nfe = Build(NfeTestData.PurchaseEntryInput());

        Assert.NotNull(nfe.infNFe.cobr);
        Assert.Equal(2, nfe.infNFe.cobr.dup.Count);
    }

    [Fact]
    public void Purchase_return_is_an_outgoing_return_referenced_only_at_item_level()
    {
        var nfe = Build(NfeTestData.PurchaseReturnInput());

        Assert.Equal(TipoNFe.tnSaida, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnDevolucao, nfe.infNFe.ide.finNFe);
        Assert.Null(nfe.infNFe.ide.NFref);
        var det = Assert.Single(nfe.infNFe.det);
        Assert.Equal(NfeTestData.EntryAccessKey, det.DFeReferenciado.chaveAcesso);
        Assert.Equal(2, det.DFeReferenciado.nItem);
        Assert.Equal(5202, det.prod.CFOP);
    }

    [Fact]
    public void Purchase_return_to_a_non_taxpayer_is_a_final_consumer_operation()
    {
        Assert.Equal(ConsumidorFinal.cfConsumidorFinal, Build(NfeTestData.PurchaseReturnInput()).infNFe.ide.indFinal);
    }

    [Fact]
    public void Purchase_return_pays_nothing_and_has_no_billing()
    {
        var nfe = Build(NfeTestData.PurchaseReturnInput());

        var payment = Assert.Single(Assert.Single(nfe.infNFe.pag).detPag);
        Assert.Equal(90, (int)payment.tPag!);
        Assert.Equal(0m, payment.vPag);
        Assert.Null(nfe.infNFe.cobr);
    }

    [Fact]
    public void Sales_return_to_a_non_taxpayer_is_not_a_final_consumer_operation()
    {
        var input = NfeTestData.ReturnInput() with
        {
            Recipient = NfeTestData.ReturnInput().Recipient with
            {
                Indicator = StateRegistrationIndicator.NonTaxpayer, StateRegistration = null,
            },
        };

        Assert.Equal(ConsumidorFinal.cfNao, Build(input).infNFe.ide.indFinal);
    }

    // --- Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §7) ---

    /// <summary>60.000,00 de produtos + frete 1.000,00 + seguro 100,00 + outras 400,00 − desconto 500,00 = 61.000,00.</summary>
    private static NfeIssueInput InputWithCharges(decimal paid = 61000m) => NfeTestData.Input() with
    {
        Items = [NfeTestData.Item() with { FreightValue = 1000m, InsuranceValue = 100m, DiscountValue = 500m, OtherExpensesValue = 400m }],
        Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", paid, new DateOnly(2026, 10, 2)),
    };

    [Fact]
    public void Line_charges_go_to_det_prod()
    {
        var prod = Build(InputWithCharges()).infNFe.det.Single().prod;

        Assert.Equal((60000m, (decimal?)1000m, (decimal?)100m, (decimal?)500m, (decimal?)400m),
            (prod.vProd, prod.vFrete, prod.vSeg, prod.vDesc, prod.vOutro));
    }

    [Fact]
    public void Totals_carry_the_charges_and_vNF_is_the_grand_total()
    {
        var total = Build(InputWithCharges()).infNFe.total.ICMSTot;

        Assert.Equal((60000m, 1000m, 100m, 500m, 400m, 61000m),
            (total.vProd, total.vFrete, total.vSeg, total.vDesc, total.vOutro, total.vNF));
    }

    [Fact]
    public void Billing_and_payment_use_the_grand_total()
    {
        var nfe = Build(InputWithCharges());

        Assert.Equal(((decimal?)61000m, (decimal?)61000m), (nfe.infNFe.cobr.fat.vOrig, nfe.infNFe.cobr.fat.vLiq));
        Assert.Equal(61000m, nfe.infNFe.cobr.dup.Sum(d => d.vDup));
        Assert.Equal(61000m, nfe.infNFe.pag.Single().detPag.Single().vPag);
    }

    [Fact]
    public void Payment_of_the_products_only_is_refused_when_the_line_has_charges()
    {
        var e = Assert.Throws<SiagroB1.Domain.Exceptions.DefaultException>(() => Build(InputWithCharges(paid: 60000m)));

        Assert.Equal("O total do pagamento não confere com o valor da nota.", e.Message);
    }

    [Fact]
    public void Line_without_charges_omits_them_and_keeps_the_totals_of_today()
    {
        // Review Focus 3: toda nota de hoje (os quatro valores em 0) sai como saía.
        var nfe = Build(NfeTestData.Input());

        var prod = nfe.infNFe.det.Single().prod;
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null), (prod.vFrete, prod.vSeg, prod.vDesc, prod.vOutro));
        var total = nfe.infNFe.total.ICMSTot;
        Assert.Equal((0m, 0m, 0m, 0m, 60000m), (total.vFrete, total.vSeg, total.vDesc, total.vOutro, total.vNF));
    }
}
