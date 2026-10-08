using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using NFe.Classes.Protocolo;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Inutilizacao;
using NFe.Classes.Servicos.Tipos;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Status;
using SiagroB1.Fiscal.Nfe;
using ConsultaEvento = NFe.Classes.Servicos.Consulta.procEventoNFe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>Retornos da SEFAZ → resultado neutro que a Application trata (sem tipos da Zeus).</summary>
public class NfeSefazResponseMapperTests
{
    private const string Key = "35261012345678000195550010000001231481516230";

    private static protNFe Protocol(int status, string reason) => new()
    {
        versao = "4.00",
        infProt = new infProt
        {
            tpAmb = TipoAmbiente.Homologacao,
            chNFe = Key, cStat = status, xMotivo = reason, nProt = "135260000000001",
            dhRecbto = new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
        },
    };

    [Fact]
    public void Processed_batch_with_authorization_returns_the_protocol()
    {
        var result = NfeSefazResponseMapper.FromAuthorization(new retEnviNFe
        {
            cStat = 104, xMotivo = "Lote processado", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Equal(100, result.StatusCode);
        Assert.Equal("135260000000001", result.Protocol);
        Assert.Contains("<protNFe", result.ProtocolXml);
        Assert.True(NfeStatusCodes.IsAuthorized(result.StatusCode));
    }

    [Fact]
    public void Processed_batch_with_rejection_returns_the_note_status()
    {
        var result = NfeSefazResponseMapper.FromAuthorization(new retEnviNFe
        {
            cStat = 104, xMotivo = "Lote processado", protNFe = Protocol(209, "Rejeição: IE do emitente inválida"),
        });

        Assert.Equal(209, result.StatusCode);
        Assert.Contains("IE do emitente", result.Reason);
    }

    [Fact]
    public void Batch_level_rejection_has_no_protocol()
    {
        var result = NfeSefazResponseMapper.FromAuthorization(new retEnviNFe
        {
            cStat = 225, xMotivo = "Rejeição: Falha no Schema XML",
        });

        Assert.Equal(225, result.StatusCode);
        Assert.Null(result.ProtocolXml);
    }

    [Fact]
    public void Consult_of_authorized_note_returns_the_protocol()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 100, xMotivo = "Autorizado o uso da NF-e", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Equal(100, result.StatusCode);
        Assert.NotNull(result.ProtocolXml);
    }

    [Fact]
    public void Consult_of_cancelled_note_reports_the_top_level_status()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 101, xMotivo = "Cancelamento de NF-e homologado", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Equal(101, result.StatusCode);
        Assert.Null(result.ProtocolXml);
        Assert.False(NfeStatusCodes.IsAuthorized(result.StatusCode));
    }

    [Fact]
    public void Missing_reason_becomes_empty()
    {
        var result = NfeSefazResponseMapper.FromStatus(new retConsStatServ { cStat = 107, xMotivo = null });

        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public void Consult_of_unknown_note_is_217()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 217, xMotivo = "Rejeição: NF-e não consta na base de dados da SEFAZ",
        });

        Assert.Equal(NfeStatusCodes.NotFound, result.StatusCode);
        Assert.Null(result.ProtocolXml);
    }

    [Fact]
    public void Service_status_maps_code_and_reason()
    {
        var result = NfeSefazResponseMapper.FromStatus(new retConsStatServ { cStat = 107, xMotivo = "Serviço em Operação" });

        Assert.Equal(NfeStatusCodes.InOperation, result.StatusCode);
    }

    [Theory]
    [InlineData(100, true, false, false)]
    [InlineData(150, true, false, false)]
    [InlineData(110, false, true, false)]
    [InlineData(301, false, true, false)]
    [InlineData(302, false, true, false)]
    [InlineData(303, false, true, false)]
    [InlineData(204, false, false, true)]
    [InlineData(539, false, false, true)]
    [InlineData(209, false, false, false)]
    public void Status_codes_are_classified(int code, bool authorized, bool denied, bool duplicate)
    {
        Assert.Equal(authorized, NfeStatusCodes.IsAuthorized(code));
        Assert.Equal(denied, NfeStatusCodes.IsDenied(code));
        Assert.Equal(duplicate, NfeStatusCodes.IsDuplicate(code));
    }

    private static infEventoRet EventReturn(int status, string reason, string? protocol = "135260000000099") => new()
    {
        cOrgao = Estado.SP, tpAmb = TipoAmbiente.Homologacao, cStat = status, xMotivo = reason, chNFe = Key,
        tpEvento = NFeTipoEvento.TeNfeCancelamento, nSeqEvento = 1, nProt = protocol,
        ProxydhRegEvento = "2026-10-05T10:00:05-03:00",
    };

    private static ConsultaEvento CancellationEvent(int status = 135) => new()
    {
        versao = "1.00",
        evento = new evento
        {
            versao = "1.00",
            infEvento = new infEventoEnv
            {
                cOrgao = Estado.SP, tpAmb = TipoAmbiente.Homologacao, chNFe = Key, tpEvento = NFeTipoEvento.TeNfeCancelamento, nSeqEvento = 1,
                verEvento = "1.00", detEvento = new detEvento { versao = "1.00", descEvento = "Cancelamento", nProt = "135260000000001", xJust = "Venda desfeita pelo cliente" },
            },
        },
        retEvento = new retEvento { versao = "1.00", infEvento = EventReturn(status, "Evento registrado e vinculado a NF-e") },
    };

    [Fact]
    public void Registered_event_uses_the_event_status_not_the_batch_status()
    {
        var result = NfeSefazResponseMapper.FromEvent(
            new retEnvEvento { cStat = 128, xMotivo = "Lote de Evento Processado",
                retEvento = [new retEvento { infEvento = EventReturn(135, "Evento registrado e vinculado a NF-e") }] },
            [CancellationEvent()]);

        Assert.Equal(135, result.StatusCode);
        Assert.True(NfeStatusCodes.IsCancellationRegistered(result.StatusCode));
        Assert.Equal("135260000000099", result.Protocol);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 10, 0, 5, TimeSpan.FromHours(-3)), result.RegisteredAt);
        Assert.Contains("<procEventoNFe", result.ProcEventXml);
        Assert.Contains("<xJust>Venda desfeita pelo cliente</xJust>", result.ProcEventXml);
    }

    [Fact]
    public void Out_of_time_registration_155_is_also_registered()
    {
        Assert.True(NfeStatusCodes.IsCancellationRegistered(155));
        Assert.False(NfeStatusCodes.IsCancellationRegistered(573));
    }

    [Fact]
    public void Rejected_event_has_no_protocol_or_xml()
    {
        var result = NfeSefazResponseMapper.FromEvent(
            new retEnvEvento { cStat = 128, xMotivo = "Lote de Evento Processado",
                retEvento = [new retEvento { infEvento = EventReturn(501, "Rejeição: Prazo de cancelamento superior ao previsto na Legislação", protocol: null) }] },
            []);

        Assert.Equal(501, result.StatusCode);
        Assert.Contains("Prazo de cancelamento", result.Reason);
        Assert.Null(result.Protocol);
        Assert.Null(result.ProcEventXml);
    }

    [Fact]
    public void Batch_level_rejection_of_the_event_keeps_the_batch_status()
    {
        var result = NfeSefazResponseMapper.FromEvent(new retEnvEvento { cStat = 215, xMotivo = "Rejeição: Falha no schema XML" }, null);

        Assert.Equal(215, result.StatusCode);
        Assert.Null(result.Protocol);
    }

    [Fact]
    public void Consult_of_cancelled_nfe_returns_101_with_the_cancellation_event()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 101, xMotivo = "Cancelamento de NF-e homologado", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
            procEventoNFe = [CancellationEvent()],
        });

        Assert.Equal(NfeStatusCodes.Cancelled, result.StatusCode);
        Assert.Null(result.ProtocolXml);
        Assert.NotNull(result.CancellationEvent);
        Assert.Equal("135260000000099", result.CancellationEvent!.Protocol);
        Assert.Equal("Venda desfeita pelo cliente", result.CancellationEvent.Justification);
        Assert.Contains("<procEventoNFe", result.CancellationEvent.ProcEventXml);
    }

    [Fact]
    public void Consult_of_cancelled_nfe_without_the_event_has_no_cancellation_event()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe { cStat = 101, xMotivo = "Cancelamento de NF-e homologado" });

        Assert.Equal(101, result.StatusCode);
        Assert.Null(result.CancellationEvent);
    }

    [Fact]
    public void Consult_of_out_of_time_cancelled_nfe_151_returns_the_cancellation_event()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 151, xMotivo = "Cancelamento de NF-e homologado fora de prazo", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
            procEventoNFe = [CancellationEvent(155)],
        });

        Assert.Equal(151, result.StatusCode);
        Assert.True(NfeStatusCodes.IsCancelledConsult(result.StatusCode));
        Assert.True(NfeStatusCodes.IsCancelledConsult(101));
        Assert.False(NfeStatusCodes.IsCancelledConsult(100));
        Assert.NotNull(result.CancellationEvent);
        Assert.Equal("135260000000099", result.CancellationEvent!.Protocol);
        Assert.Equal("Venda desfeita pelo cliente", result.CancellationEvent.Justification);
    }

    private static ConsultaEvento CorrectionEvent(int sequence, string text, int status = 135) => new()
    {
        versao = "1.00",
        evento = new evento
        {
            versao = "1.00",
            infEvento = new infEventoEnv
            {
                cOrgao = Estado.SP, tpAmb = TipoAmbiente.Homologacao, chNFe = Key,
                tpEvento = NFeTipoEvento.TeNfeCartaCorrecao, nSeqEvento = sequence, verEvento = "1.00",
                detEvento = new detEvento { versao = "1.00", xCorrecao = text },
            },
        },
        retEvento = new retEvento
        {
            versao = "1.00",
            infEvento = EventReturn(status, "Evento registrado e vinculado a NF-e", $"13526000000020{sequence}"),
        },
    };

    [Fact]
    public void Registered_correction_carries_sequence_and_text()
    {
        var proc = CorrectionEvent(2, "Placa correta XYZ9K87");
        var result = NfeSefazResponseMapper.FromEvent(
            new retEnvEvento { cStat = 128, xMotivo = "Lote de evento processado", retEvento = [proc.retEvento] }, [proc]);

        Assert.Equal(135, result.StatusCode);
        Assert.Equal(2, result.Sequence);
        Assert.Equal("Placa correta XYZ9K87", result.CorrectionText);
        Assert.Equal("135260000000202", result.Protocol);
        Assert.Contains("<procEventoNFe", result.ProcEventXml);
        Assert.True(NfeStatusCodes.IsEventRegistered(result.StatusCode));
    }

    [Fact]
    public void Consult_of_authorized_note_returns_the_registered_corrections()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 100, xMotivo = "Autorizado o uso da NF-e", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
            procEventoNFe = [CorrectionEvent(1, "Primeira correcao do texto"), CorrectionEvent(2, "Segunda correcao do texto"),
                CorrectionEvent(3, "Recusada pela SEFAZ aqui", status: 573)],
        });

        Assert.Equal(100, result.StatusCode);
        Assert.Equal("135260000000001", result.Protocol);
        Assert.Equal([1, 2], result.Corrections!.Select(c => c.Sequence!.Value));
        Assert.Equal("Segunda correcao do texto", result.Corrections![1].CorrectionText);
    }

    [Fact]
    public void Consult_of_cancelled_note_returns_cancellation_and_corrections()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 101, xMotivo = "Cancelamento de NF-e homologado",
            procEventoNFe = [CorrectionEvent(1, "Correcao antes de cancelar"), CancellationEvent()],
        });

        Assert.NotNull(result.CancellationEvent);
        Assert.Single(result.Corrections!);
    }

    [Fact]
    public void Consult_without_events_has_no_corrections()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 100, xMotivo = "Autorizado o uso da NF-e", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Empty(result.Corrections ?? []);
    }

    private const string VoidRequestXml =
        "<inutNFe versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\"><infInut Id=\"ID35261234567800019555001000000233000000233\">" +
        "<tpAmb>2</tpAmb><xServ>INUTILIZAR</xServ><cUF>35</cUF><ano>26</ano><CNPJ>12345678000195</CNPJ><mod>55</mod>" +
        "<serie>1</serie><nNFIni>233</nNFIni><nNFFin>233</nNFFin><xJust>NF-e rejeitada e documento cancelado</xJust></infInut></inutNFe>";

    private static retInutNFe VoidResponse(int status, string? protocol) => new()
    {
        versao = "4.00",
        infInut = new infInutRet
        {
            tpAmb = TipoAmbiente.Homologacao, verAplic = "SP_NFE_PL009", cStat = status,
            xMotivo = status == 102 ? "Inutilização de número homologado" : "Rejeição: NF-e já está inutilizada na Base de dados da SEFAZ",
            nProt = protocol,
        },
    };

    [Fact]
    public void Homologated_void_number_carries_protocol_and_proc_xml()
    {
        var result = NfeSefazResponseMapper.FromVoidNumber(VoidRequestXml, VoidResponse(102, "135260000000777"));

        Assert.Equal(102, result.StatusCode);
        Assert.Equal("135260000000777", result.Protocol);
        Assert.Contains("<procInutNFe", result.ProcXml);
        Assert.Contains("<nNFIni>233</nNFIni>", result.ProcXml);
        Assert.Contains("<nProt>135260000000777</nProt>", result.ProcXml);
    }

    [Fact]
    public void Void_number_refusal_has_no_proc_xml()
    {
        var result = NfeSefazResponseMapper.FromVoidNumber(VoidRequestXml, VoidResponse(256, null));

        Assert.Equal(256, result.StatusCode);
        Assert.Null(result.Protocol);
        Assert.Null(result.ProcXml);
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(563, true)]
    [InlineData(102, false)]
    [InlineData(241, false)]
    public void Already_voided_codes(int code, bool expected) =>
        Assert.Equal(expected, NfeStatusCodes.IsNumberAlreadyVoided(code));
}
