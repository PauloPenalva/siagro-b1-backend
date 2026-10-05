using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Acesso do pipeline da NF-e ao documento e aos XMLs dele — a única parte que muda entre o documento
/// de saída e o de entrada. O resto (reserva, assinatura, transmissão, retorno, consulta) é comum.
/// </summary>
public interface INfeDocumentStore<TDocument> where TDocument : class, INfeDocument
{
    /// <summary>Mensagem do <c>NotFoundException</c> quando o documento não existe.</summary>
    string NotFoundMessage { get; }

    /// <summary>Rastreado, com os itens (emissão).</summary>
    Task<TDocument?> FindWithItemsAsync(Guid key);

    /// <summary>Rastreado, só o cabeçalho (retorno, consulta).</summary>
    Task<TDocument?> FindAsync(Guid key);

    /// <summary>Sem rastreamento (conclusão, download).</summary>
    Task<TDocument?> FindReadOnlyAsync(Guid key);

    /// <summary>Acrescenta um XML ao documento; grava no próximo <c>SaveChanges</c>.</summary>
    void AddXml(TDocument document, NfeXmlKind kind, string xml);

    /// <summary>XMLs assinados do documento, o mais novo primeiro.</summary>
    Task<IReadOnlyList<string>> SignedXmlsAsync(Guid key);

    /// <summary>procNFe autorizado mais recente; sem ele, <c>NotFoundException</c>.</summary>
    Task<string> LatestAuthorizedXmlAsync(Guid key);
}
