using DFe.Classes.Flags;
using NFe.Classes.Protocolo;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Status;
using SiagroB1.Fiscal.Nfe;

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
}
