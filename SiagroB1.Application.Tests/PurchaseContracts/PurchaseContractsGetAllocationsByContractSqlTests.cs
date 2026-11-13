using System.Reflection;
using System.Text.RegularExpressions;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A aba "Entregas" do contrato de compra travava (timeout de 30 s) atrás de qualquer escrita em andamento:
/// o banco roda READ COMMITTED com RCSI desligado e só uma tabela tinha NOLOCK. Estes testes garantem que
/// TODA tabela referenciada (FROM/JOIN, inclusive nas subconsultas do OUTER APPLY) leva <c>WITH (NOLOCK)</c>.
/// As constantes são privadas e o projeto não tem InternalsVisibleTo, então são lidas por reflexão.
/// </summary>
public class PurchaseContractsGetAllocationsByContractSqlTests
{
    private static readonly Regex TableReference = new(
        @"\b(FROM|JOIN)\s+(\w+)(?:\s+(?!WITH\b|ON\b|LEFT\b|JOIN\b|WHERE\b|OUTER\b)(\w+))?\s*(WITH\s*\(\s*NOLOCK\s*\))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ReadSql(string field) =>
        (string)typeof(PurchaseContractsGetAllocationsByContractService)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    // Remove comentários de linha (-- ...) para não confundir "FROM"/"JOIN" citados em texto.
    private static string StripComments(string sql) => Regex.Replace(sql, @"--[^\r\n]*", "");

    [Theory]
    [InlineData("SQL")]
    [InlineData("LinksSql")]
    public void Every_table_reference_has_nolock_hint(string field)
    {
        var sql = StripComments(ReadSql(field));
        var matches = TableReference.Matches(sql);

        Assert.NotEmpty(matches);
        var missing = matches.Where(m => !m.Groups[4].Success).Select(m => m.Groups[2].Value + " " + m.Groups[3].Value).ToList();
        Assert.True(missing.Count == 0, $"{field}: tabelas sem WITH (NOLOCK): {string.Join(", ", missing)}");
    }

    [Fact]
    public void Main_query_references_all_expected_tables()
    {
        var sql = StripComments(ReadSql("SQL"));
        // 1 FROM + 19 LEFT JOIN + 3 do 1o OUTER APPLY (PFR, PII, PI) + PA2 + RG = 25 referências.
        Assert.Equal(25, TableReference.Matches(sql).Count);
    }

    /// <summary>
    /// GAC-1216: a coluna "Carga" da aba Entregas. O romaneio de compra não carrega ShipmentLoadKey —
    /// a carga é a da perna de venda da Expedição. Quando a liberação da carga foi trocada
    /// (GAC-1177) a perna original sai da carga, e o elo passa a ser o documento da troca; os
    /// romaneios de quebra/sobra de descarga chegam à carga pela nota conferida.
    /// </summary>
    [Fact]
    public void Main_query_resolves_the_load_code_of_each_delivery()
    {
        var sql = StripComments(ReadSql("SQL"));

        Assert.Matches(@"ELSE COALESCE\(SLOAD\.Code, CHGLOAD\.Code, DLOAD\.Code\)\s+END AS LoadCode", sql);
        Assert.Contains("SLOAD.[Key] = SALES.ShipmentLoadKey", sql);
        Assert.Contains("CHG.[Key] = COALESCE(ST.ReplacedByShippingReleaseChangeKey, ST.ShippingReleaseChangeKey)", sql);
        Assert.Contains("CHGLOAD.[Key] = CHG.ShipmentLoadKey", sql);
    }

    /// <summary>
    /// GAC-1212: a coluna "Quantidade" mostra o sinal do ledger — o que abate o contrato (Compra,
    /// Compl.Qtd., Sobra Descarga) positivo e o que devolve saldo (Dev.Compra, Quebra Descarga)
    /// negativo, como no contrato de venda. Antes a consulta invertia o sinal das devoluções e a
    /// conferência do saldo exigia ler o tipo de cada linha.
    /// </summary>
    [Fact]
    public void Main_query_shows_the_allocation_volume_with_its_ledger_sign()
    {
        var sql = StripComments(ReadSql("SQL"));

        Assert.Contains("PA.Volume AS Volume", sql);
        Assert.DoesNotContain("PA.Volume * -1", sql);
    }

    /// <summary>
    /// GAC-1214 rodada 3: devolução e reentrega vêm do próprio ledger (alocações derivadas da liberação
    /// de devolução). O gerador 12 é "Devolução"/LoadRefusal; o 7/13/12 que consome a liberação é
    /// "Reentrega"/ReturnRedelivery. A liberação do gerador é filtrada pelo contrato da linha, para que
    /// um gerador com liberações em vários contratos não duplique nem contamine as linhas.
    /// </summary>
    [Fact]
    public void Main_query_labels_sales_return_rows()
    {
        var sql = StripComments(ReadSql("SQL"));

        Assert.Contains("'Devolução'", sql);
        Assert.Contains("'Reentrega'", sql);
        Assert.Contains("'LoadRefusal'", sql);
        Assert.Contains("'ReturnRedelivery'", sql);
        Assert.Matches(@"SHIPMENT_RELEASES\s+\w+\s+WITH\s*\(\s*NOLOCK\s*\)", sql);
        Assert.Contains("RG.PurchaseContractKey = PA.PurchaseContractKey", sql);
        Assert.Contains("RCON.PurchaseContractKey = PA.PurchaseContractKey", sql);
        Assert.Contains("RefusedFromShipmentLoadKey", sql);
    }

    /// <summary>Uma linha de saída por linha do ledger: o DISTINCT não pode fundir duas alocações iguais.</summary>
    [Fact]
    public void Main_query_selects_the_allocation_key()
    {
        Assert.Contains("PA.[Key] AS AllocationKey", StripComments(ReadSql("SQL")));
    }

    [Fact]
    public void Main_query_checks_derived_rows_before_the_transaction_type()
    {
        var sql = Regex.Replace(StripComments(ReadSql("SQL")), @"\s+", " ");

        Assert.Contains("CASE WHEN RGEN.ReleaseKey IS NOT NULL THEN N'Devolução' WHEN RCON.[Key] IS NOT NULL THEN N'Reentrega' WHEN ST.TransactionType = 8", sql);
        Assert.Contains("CASE WHEN RGEN.ReleaseKey IS NOT NULL THEN 'LoadRefusal' WHEN RCON.[Key] IS NOT NULL THEN 'ReturnRedelivery' WHEN ST.TransactionType = 16", sql);
    }

    [Fact]
    public void Redelivery_reason_falls_back_when_the_generator_code_is_missing()
    {
        var sql = StripComments(ReadSql("SQL"));

        Assert.Contains("N'Saída da devolução ao armazém'", sql);
        Assert.DoesNotContain("COALESCE(STGEN.Code, N'')", sql);
    }

    [Fact]
    public void Returns_side_query_is_gone()
    {
        Assert.Null(typeof(PurchaseContractsGetAllocationsByContractService)
            .GetField("ReturnsSql", BindingFlags.NonPublic | BindingFlags.Static));
    }
}
