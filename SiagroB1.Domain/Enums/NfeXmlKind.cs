namespace SiagroB1.Domain.Enums;

/// <summary>Tipo do XML da NF-e guardado com o documento (saída ou entrada).</summary>
public enum NfeXmlKind
{
    Signed = 1,
    Authorized = 2,
    /// <summary>procNFe da NF-e denegada: o emitente guarda o XML denegado.</summary>
    Denied = 3,
}
