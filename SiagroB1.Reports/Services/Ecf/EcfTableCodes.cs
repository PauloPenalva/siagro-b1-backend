namespace SiagroB1.Reports.Services.Ecf;

/// <summary>Linha de tabela dinâmica da ECF: código e descrição como na tabela do PVA.</summary>
public sealed record EcfTableLine(string Code, string Description);

/// <summary>
/// Códigos das linhas das tabelas dinâmicas da ECF que o Siagro preenche (Lucro Real, PJ em geral — tabelas "A").
/// Num lugar só de propósito: as tabelas dinâmicas são publicadas pela RFB com o PVA de cada leiaute e os códigos
/// abaixo seguem o Manual de Orientação da ECF como conhecido no desenvolvimento — CONFERIR contra as tabelas do PVA
/// do leiaute do ano antes da primeira entrega (o PVA aponta código inexistente na importação).
/// As adições, exclusões e compensações do M300/M350 usam o código informado pelo usuário em cada ajuste
/// (EcfAdjustment.TableCode); aqui ficam só as linhas que o sistema calcula.
/// </summary>
public static class EcfTableCodes
{
    // M300A — Demonstração do Lucro Real (Parte A do e-Lalur): lucro antes do IRPJ (TIPO_LANCAMENTO "L").
    public static readonly EcfTableLine M300BookProfit = new("1", "Lucro Líquido antes do IRPJ");

    // M350A — Demonstração da Base de Cálculo da CSLL (Parte A do e-Lacs): lucro antes da CSLL ("L").
    public static readonly EcfTableLine M350BookProfit = new("1", "Lucro Líquido antes da CSLL");

    // N500A — Base de cálculo do IRPJ sobre o lucro real após as compensações de prejuízos.
    public static readonly EcfTableLine N500BookProfit = new("1", "Lucro Líquido antes do IRPJ");
    public static readonly EcfTableLine N500Additions = new("2", "(+) Adições");
    public static readonly EcfTableLine N500Exclusions = new("3", "(-) Exclusões");
    public static readonly EcfTableLine N500AdjustedProfit = new("4", "Lucro Real antes da Compensação de Prejuízos");
    public static readonly EcfTableLine N500LossOffset = new("5", "(-) Compensação de Prejuízos Fiscais");
    public static readonly EcfTableLine N500TaxableIncome = new("6", "Lucro Real");

    // N620A — IRPJ mensal por estimativa (balancete de suspensão/redução, períodos A01..A12).
    public static readonly EcfTableLine N620Base = new("1", "Base de Cálculo do IRPJ (balancete acumulado)");
    public static readonly EcfTableLine N620Tax = new("3", "IRPJ à Alíquota de 15%");
    public static readonly EcfTableLine N620Surtax = new("4", "Adicional");
    public static readonly EcfTableLine N620Due = new("20", "IRPJ Devido no Período Acumulado");
    public static readonly EcfTableLine N620PreviouslyDue = new("22", "(-) IRPJ Devido em Meses Anteriores");
    public static readonly EcfTableLine N620Payable = new("26", "IRPJ a Pagar");

    // N630A — IRPJ com base no lucro real (A00 ou T01..T04).
    public static readonly EcfTableLine N630Base = new("1", "Base de Cálculo do IRPJ");
    public static readonly EcfTableLine N630Tax = new("3", "IRPJ à Alíquota de 15%");
    public static readonly EcfTableLine N630Surtax = new("4", "Adicional");
    public static readonly EcfTableLine N630Due = new("20", "Imposto de Renda Devido");
    public static readonly EcfTableLine N630Estimates = new("24", "(-) Imposto de Renda Mensal Pago por Estimativa");
    public static readonly EcfTableLine N630Payable = new("26", "Imposto de Renda a Pagar");

    // N650A — Base de cálculo da CSLL após as compensações da base negativa.
    public static readonly EcfTableLine N650BookProfit = new("1", "Lucro Líquido antes da CSLL");
    public static readonly EcfTableLine N650Additions = new("2", "(+) Adições");
    public static readonly EcfTableLine N650Exclusions = new("3", "(-) Exclusões");
    public static readonly EcfTableLine N650AdjustedProfit = new("4", "Base de Cálculo antes da Compensação da Base Negativa");
    public static readonly EcfTableLine N650LossOffset = new("5", "(-) Compensação de Base de Cálculo Negativa");
    public static readonly EcfTableLine N650Base = new("6", "Base de Cálculo da CSLL");

    // N660A — CSLL mensal por estimativa (A01..A12).
    public static readonly EcfTableLine N660Base = new("1", "Base de Cálculo da CSLL (balancete acumulado)");
    public static readonly EcfTableLine N660Tax = new("2", "CSLL à Alíquota de 9%");
    public static readonly EcfTableLine N660PreviouslyDue = new("15", "(-) CSLL Devida em Meses Anteriores");
    public static readonly EcfTableLine N660Payable = new("19", "CSLL a Pagar");

    // N670A — CSLL com base no lucro real (A00 ou T01..T04).
    public static readonly EcfTableLine N670Base = new("1", "Base de Cálculo da CSLL");
    public static readonly EcfTableLine N670Tax = new("2", "CSLL à Alíquota de 9%");
    public static readonly EcfTableLine N670Estimates = new("17", "(-) CSLL Mensal Paga por Estimativa");
    public static readonly EcfTableLine N670Payable = new("19", "CSLL a Pagar");
}
