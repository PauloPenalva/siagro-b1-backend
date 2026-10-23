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

    /// <summary>NF-e próprias canceladas (02), denegadas (04) ou inutilizadas (05) da filial no mês.</summary>
    public List<SpedVoidDocument> VoidDocuments { get; set; } = [];
}

/// <summary>Inconsistência da verificação: bloqueante impede gerar o arquivo.</summary>
public class EfdContributionsIssueDto
{
    public bool Blocking { get; set; }
    public string Message { get; set; } = "";

    /// <summary>Código/documento a que a mensagem se refere (filial, natureza, nota/item).</summary>
    public string Reference { get; set; } = "";
}

/// <summary>Totais da apuração do mês (blocos M200/M600): devido, crédito descontado e a recolher.</summary>
public class EfdContributionsTotalsDto
{
    public decimal PisDue { get; set; }
    public decimal PisCredit { get; set; }
    public decimal PisPayable { get; set; }
    public decimal CofinsDue { get; set; }
    public decimal CofinsCredit { get; set; }
    public decimal CofinsPayable { get; set; }
}

/// <summary>Prévia antes de gerar: inconsistências (bloqueantes e avisos) e totais.</summary>
public class EfdContributionsCheckDto
{
    public List<EfdContributionsIssueDto> Issues { get; set; } = [];
    public EfdContributionsTotalsDto Totals { get; set; } = new();
}
