using SiagroB1.Domain.Entities;
using SiagroB1.Reports.Services.Sped;

namespace SiagroB1.Reports.Dtos;

/// <summary>Pedido da EFD Contribuições: qualquer filial da empresa (CNPJ raiz) e o mês.</summary>
public class EfdContributionsRequest
{
    public string BranchCode { get; set; } = "";
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>0 = original, 1 = retificadora.</summary>
    public int Purpose { get; set; }

    /// <summary>Recibo da escrituração anterior (obrigatório na retificadora).</summary>
    public string? PreviousReceiptNumber { get; set; }
}

/// <summary>Tudo o que a EFD Contribuições do mês precisa, agrupado por estabelecimento.</summary>
public class EfdContributionsData
{
    /// <summary>Matriz (ordem 0001) do CNPJ raiz; nulo quando não cadastrada.</summary>
    public Branch? Declarant { get; set; }

    /// <summary>Dados do contabilista da matriz; nulo quando não cadastrados.</summary>
    public SpedFiscalSettings? Settings { get; set; }

    public List<EfdEstablishment> Establishments { get; set; } = [];

    /// <summary>Naturezas referenciadas pelas linhas, por código.</summary>
    public Dictionary<int, Usage> Usages { get; set; } = [];
}

/// <summary>Uma filial do CNPJ raiz com suas linhas escrituráveis e o cadastro do bloco 0.</summary>
public class EfdEstablishment
{
    public Branch Branch { get; set; } = null!;
    public List<FiscalDocumentLineDto> Lines { get; set; } = [];
    public SpedMasterData MasterData { get; set; } = new();
}
