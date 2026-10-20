using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// XML completo (procNFe) de um DF-e recebido. Sem o XML guardado, baixa por chave; documento ainda sem manifestação
/// recebe a Ciência da Operação automaticamente, que é o que a SEFAZ exige para liberar o XML.
/// </summary>
public class ReceivedDfeXmlService(ReceivedDfeManifestService manifest)
{
    public async Task<(byte[] Content, string FileName)> GetAsync(int id, CancellationToken ct = default)
    {
        var row = await manifest.LoadAsync(id, ct);

        if (row.Xml is null)
            row = row.ManifestationStatus == DfeManifestationStatus.None
                ? await manifest.ExecuteAsync(id, NfeManifestationType.Acknowledgement, null, ct) // já tenta baixar o XML
                : await manifest.DownloadXmlAsync(id, ct);

        if (row.Xml is null)
            throw new DefaultException("O XML ainda não está disponível na SEFAZ. Tente novamente mais tarde.");

        return (System.Text.Encoding.UTF8.GetBytes(row.Xml), $"{row.AccessKey}-procNFe.xml");
    }
}
