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
        Assert.Null(icms.pRedBC);
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
}
