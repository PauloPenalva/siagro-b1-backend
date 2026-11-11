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
    [InlineData("ReturnsSql")]
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
        // 1 FROM + 12 LEFT JOIN + 3 do 1o OUTER APPLY (PFR, PII, PI) + PA2 = 17 referências.
        Assert.Equal(17, TableReference.Matches(sql).Count);
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
    /// GAC-1214: a carga recusada para um armazém aparece no contrato como "Devolução", sem mexer no
    /// saldo. O romaneio 12 não tem alocação nem ShipmentReleaseKey — o elo com o contrato é a
    /// liberação de devolução (Origin = SalesReturn) que ele gerou. Romaneio cancelado não aparece.
    /// </summary>
    [Fact]
    public void Returns_query_links_refused_loads_through_the_sales_return_release()
    {
        var sql = StripComments(ReadSql("ReturnsSql"));

        Assert.Contains("SR.GeneratedByStorageTransactionKey", sql);
        Assert.Contains("SR.PurchaseContractKey = @purchaseContractKey", sql);
        Assert.Contains("SR.Origin = 2", sql);
        Assert.Contains("ST.TransactionType = 12", sql);
        Assert.Contains("ST.TransactionStatus <> 2", sql);
        Assert.Contains("RefusedFromShipmentLoadKey", sql);
    }
}
