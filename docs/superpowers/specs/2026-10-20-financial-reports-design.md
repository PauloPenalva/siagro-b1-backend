# Relatórios financeiros: carteira, baixas e extrato do parceiro — design

Data: 2026-10-20 · Repos: `siagro-b1-backend` + `siagro-b1-frontend` · Modos: STANDALONE e SAPB1 (o financeiro vive no
banco do Siagro nos dois, como o Extrato da Conta).

## 1. Intenção

O financeiro precisa imprimir (PDF) a carteira e o movimento por parceiro **numa data-base passada**: hoje é 20/10, o
usuário emite "01/10 a 10/10" e vê a carteira como ela estava em 10/10. Baixa feita em 11/10 não existe para esse
relatório — o título aparece em aberto. Isso é o que diferencia estes relatórios das telas atuais (que mostram o saldo de
hoje, `NetAmount − SettledAmount`).

## 2. Os cinco relatórios (cada um com menu próprio sob "Financeiro")

| Menu (pt-BR) | Chave de rota/menu (sugestão) | Direção |
|---|---|---|
| Contas a Receber | `receivablesReport` | Receivable |
| Contas a Pagar | `payablesReport` | Payable |
| Baixas de Recebíveis | `receivableSettlementsReport` | Receivable |
| Baixas de Pagáveis | `payableSettlementsReport` | Payable |
| Extrato do Parceiro | `partnerStatementReport` | ambas |

Receber/Pagar compartilham serviço e template (parâmetro direção); idem as duas Baixas. Cada tela: filtros + botão
"Gerar PDF", no padrão da tela de Extrato da Conta (`financialAccountStatementReport`). Menus via migration do
CommonContext, `StandaloneOnly = false`, `ROLE_MENUS` para ADMIN, Key igual ao name da rota no `manifest.json` (ver
`20261019182733_AddFinancialAccountMovementsMenus.cs`); Orders = próximos livres sob `financial`.

## 3. Regras decididas pelo usuário

**R1 — Data-base.** A data final do período é a data-base. Uma baixa (linha de `FINANCIAL_SETTLEMENTS`, incluindo estornos,
que são linhas negativas com data própria) conta só se `SettlementDate <= data-base`. Baixa parcial em 05/10 conta;
baixa em 11/10 não. Saldo na data-base = valor do título − Σ baixas até a data-base.

**R2 — Período da carteira.** Em Contas a Receber/Pagar, o período (início–fim) filtra por **vencimento**. Título emitido
depois da data-base (`DocumentDate > data-base`) não aparece (ainda não existia). Mostra só títulos com saldo > 0 na data-base.

**R3 — Período das baixas.** Em Baixas, o período filtra pela data da baixa.

**R4 — Parâmetro Firmes × Provisórios** (obrigatório, nunca os dois juntos — evita contar o mesmo dinheiro duas vezes):
- **Firmes:** títulos de natureza Firm, PurchaseReturn, SalesReturn, Advance, TaxWithholding e todos os sem contrato
  (manual, outra compra, saída sem contrato, washout). Provisórios ficam fora.
- **Provisórios:** o provisório do contrato entra **no lugar** dos firmes/devoluções de contrato (`IsContractFirmDocument`):
  esses ficam fora. Os demais (adiantamento, retenção, manual, sem contrato, washout) entram normalmente.

**R5 — Composição no modo Firmes.** Compor um firme no provisório grava hoje uma baixa `InvoiceOffset` com `CompositionKey`
no firme (e seu estorno `Reversal` ao desfazer). No modo Firmes essas linhas **não contam como baixa** — o firme composto
continua em aberto até o provisório ser pago. O dinheiro baixado no provisório (baixas com `SettlementDate <= data-base`,
líquidas de estorno) é **distribuído entre os firmes compostos nele, por ordem de vencimento** (empate: emissão, depois
código), até o valor composto de cada um (`FINANCIAL_DOCUMENT_COMPOSITIONS.Amount`; devolução entra negativa, como na
composição). Nos relatórios de Baixas e no Extrato, a fatia distribuída aparece no firme com a data, a conta e a origem
da baixa real do provisório. No modo Provisórios as linhas de abatimento também são irrelevantes (firmes estão fora).
Dação, encontro de contas (Netting), aplicação de adiantamento e demais origens contam normalmente.

**R6 — Extrato do Parceiro, formato "extrato bancário".** Parceiro obrigatório, período, direção (A Receber, A Pagar ou
Ambos — sugestão minha), Firmes/Provisórios. Saldo anterior (saldo em aberto em início−1 dia, regras R1/R4/R5); depois,
em ordem de data: cada título emitido no período (lança o valor) e cada baixa do período (abate), com saldo acumulado; saldo
final = saldo em aberto na data final. Em "Ambos", a receber soma e a pagar subtrai (saldo positivo = parceiro deve à empresa).

**R7 — Devolução de contrato no modo Firmes (decisão do usuário).** A devolução (PurchaseReturn/SalesReturn) composta
aparece na carteira como linha **negativa**. Na distribuição de R5 ela é abatida **primeiro** dos firmes do mesmo
provisório (reduz o quanto eles absorvem, na ordem de vencimento); ela sai da carteira quando o dinheiro baixado no
provisório cobrir o líquido composto (firmes − devoluções). Devolução não composta segue o próprio ledger (Netting etc.).

**R8 — Valor do provisório no modo Provisórios (decisão do usuário).** O provisório mostra só a parte **composta
(faturada) na data-base**, reconstruída pelo ledger (linhas `InvoiceOffset`/`Reversal` com `CompositionKey` dos firmes
compostos nele, com data ≤ data-base; devolução negativa). Saldo = composto na data-base − baixas do provisório até a
data-base. Provisório sem nada composto na data-base não aparece. Motivo: `NetAmount` do provisório muda sem histórico.

**Decisões do builder-check tomadas por mim (Claude), revisáveis:**
- **C1 — Origem das baixas do provisório em R5:** todas as baixas do provisório (dinheiro, aplicação de adiantamento,
  dação, encontro de contas, e seus estornos) são distribuídas aos firmes; a fatia mantém a origem da baixa real, que vira
  a "forma" no relatório de Baixas.
- **C2 — Débito/crédito no Extrato:** a receber: emissão = débito, baixa = crédito; a pagar: emissão = crédito, baixa =
  débito. Saldo = débitos − créditos (positivo = parceiro deve à empresa). Estorno aparece negativo na mesma coluna da baixa
  que estorna. Devolução de contrato entra na coluna oposta à do título normal.
- **C3 — Datas:** comparações por data (sem hora) em `DocumentDate` e `SettlementDate`.

## 4. Layout proposto (aceito pelo usuário como "está bom")

- **Carteira (Receber/Pagar):** agrupado por parceiro com subtotal; colunas documento (Code), origem (contrato/nota/
  manual — `OriginDocNumber`/contrato), emissão, vencimento, valor, baixado até a data-base, saldo, dias de atraso
  (data-base − vencimento, só se > 0). Total geral. Filtros: período de vencimento, Firmes/Provisórios, parceiro opcional.
- **Baixas:** agrupado por data da baixa; colunas parceiro, título, conta, forma (origem da baixa em pt-BR), valor,
  juros, multa, desconto. Totais por dia e geral. Filtros: período da baixa, Firmes/Provisórios, parceiro e conta opcionais.
- **Extrato:** cabeçalho com parceiro e período; colunas data, histórico, documento, débito, crédito, saldo.

## 5. Restrições e convenções

- Serviço de relatório em `SiagroB1.Reports` (padrão `FinancialAccountStatementReportService`: `Validate` estático,
  controller `[Route("/reports/...")]`, template `.frx` em `Reports/Templates`, `IFastReportService.GeneratePdfAsync`).
- O cálculo da posição na data-base fica numa **consulta/serviço único reutilizado pelos quatro relatórios** (como
  `FinancialAccountMovementsQuery`), testável em `SiagroB1.Application.Tests`. R5 é a parte com mais risco: testes
  dedicados (pagamento parcial do provisório, vários firmes, devolução negativa, estorno de baixa, desfazer composição,
  baixa após a data-base).
- Identificadores em inglês; textos em pt-BR. Moeda: valores na moeda do título; se houver títulos USD, coluna/total por
  moeda (sem conversão).
- Nome do parceiro: `CardName` gravado no título (vale nos dois modos).

## 6. Fora de escopo

Exportação Excel, conversão cambial, gráficos, envio por e-mail, alteração do ledger ou da composição.

## 7. Limites conhecidos

- Título **cancelado** não tem data de cancelamento: some de toda data-base. Aceitável — só cancela título Manual sem baixa.
- Composição desfeita depois da data-base: a tabela de composição já não a tem. Para R5, a distribuição usa as
  composições vigentes cuja `InvoiceOffset` tenha data ≤ data-base e não estornada até a data-base (o ledger é a fonte;
  o builder pode reconstruir o valor composto na data a partir das linhas `InvoiceOffset`/`Reversal` com `CompositionKey`).

## 8. Deixado ao builder

Nomes de classes/rotas, Orders dos menus, desenho do `.frx`, controle da tela de filtros, permissão (seguir o padrão das
telas de relatório existentes).
