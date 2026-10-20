using DFe.Classes.Entidades;
using NFe.Classes.Servicos.Evento;
using NFe.Classes.Servicos.Tipos;
using NFe.Utils;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Monta o evento de manifestação do destinatário (210200/210210/210220/210240) para o Ambiente Nacional (cOrgao 91).
/// Fica nosso (e não na sobrecarga por chave da Zeus) para o teste de XSD exercitar exatamente o que o client envia.
/// Devolve o evento sem assinatura: quem envia assina.
/// </summary>
internal static class NfeManifestationEventBuilder
{
    public static evento Build(NfeManifestationRequest request, ConfiguracaoServico configuration)
    {
        var (eventType, description) = request.Type switch
        {
            NfeManifestationType.Confirmation => (NFeTipoEvento.TeMdConfirmacaoDaOperacao, "Confirmacao da Operacao"),
            NfeManifestationType.Acknowledgement => (NFeTipoEvento.TeMdCienciaDaOperacao, "Ciencia da Operacao"),
            NfeManifestationType.Unknown => (NFeTipoEvento.TeMdDesconhecimentoDaOperacao, "Desconhecimento da Operacao"),
            NfeManifestationType.NotPerformed => (NFeTipoEvento.TeMdOperacaoNaoRealizada, "Operacao nao Realizada"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Tipo de manifestação inválido."),
        };

        var taxId = new string(request.RecipientTaxId.Where(char.IsLetterOrDigit).ToArray());
        var info = new infEventoEnv
        {
            Id = $"ID{(int)request.Type}{request.AccessKey}01",
            cOrgao = Estado.AN,
            tpAmb = configuration.tpAmb,
            chNFe = request.AccessKey,
            dhEvento = request.EventAt,
            tpEvento = eventType,
            nSeqEvento = 1,
            verEvento = "1.00",
            detEvento = new detEvento
            {
                versao = "1.00",
                descEvento = description,
                xJust = request.Type == NfeManifestationType.NotPerformed ? request.Justification : null,
            },
        };

        if (taxId.Length == 11)
            info.CPF = taxId;
        else
            info.CNPJ = taxId;

        return new evento { versao = "1.00", infEvento = info };
    }
}
