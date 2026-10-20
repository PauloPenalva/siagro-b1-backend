# Transferência entre contas, conta sem filial e adiantamento sem contrato — design

Data: 2026-10-19 · Repos: `siagro-b1-backend` + `siagro-b1-frontend`

Três entregas independentes do módulo Financeiro, nesta ordem (cada uma pode ser commitada sozinha):

1. Conta financeira sem filial (ajuste de tela)
2. Transferência entre contas financeiras
3. Adiantamento sem contrato

## Por quê

- Há contas financeiras usadas para títulos de várias filiais; a tela não deixa limpar a filial depois de escolhida.
- Hoje não há como registrar dinheiro movido de uma conta para outra (banco → caixa etc.); o extrato e a conciliação ficam furados ou exigem dois lançamentos avulsos soltos.
- Adiantamento hoje exige contrato aprovado. Na prática se adianta a prestador de serviço, ou se recebe de cliente antes de existir (ou de se saber) o contrato. O valor é consumido abatendo um título que vem depois.

## Estado atual (levantado no código em 2026-10-19)

- `FinancialAccount.BranchCode` já é `string?` (nulo = "conta corporativa"); create/update não validam filial; nenhum serviço cruza a filial da conta com a do título. O problema é só a tela (`view/financialAccounts/fragments/Form.fragment.xml`, Input "Filial" com value help).
- Extrato = `FinancialSettlement` (baixas de títulos) + `FinancialAccountEntry` (lançamento avulso com `Amount` com sinal, `Description`, `DocumentReference`, `Notes`, `ReconciliationKey`), unidos em `FinancialAccountMovementsQuery.StatementAsync`. Lançamento conciliado não pode ser excluído.
- Adiantamento = `FinancialDocument` com `Nature = Advance`; `CardCode` obrigatório; `PurchaseContractKey`/`SalesContractKey` já anuláveis. Quem exige contrato: `FinancialAdvancesCreateService` (parâmetros, status Aprovado, copia parceiro/moeda/filial/condição do contrato), `FinancialDocumentsApplyAdvanceService` (alvo Provisório com contrato; mesmo contrato), `FinancialDocumentsGetAdvanceCandidatesService`, e o diálogo `CreateAdvanceDialog.fragment.xml` (contrato `required`). "Nasce pago" chama `FinancialDocumentsSettleService` na mesma transação. Vínculo a outro contrato: `FinancialAdvancesRelinkContractService` (origem `AdvanceTransfer`).

## 1. Conta financeira sem filial

- Na tela, o campo Filial pode ser limpo e salvo vazio (nulo no banco, não string vazia).
- Conta sem filial serve para títulos de qualquer filial; conta com filial continua sem restrição (comportamento atual mantido — nenhuma validação nova).
- Lista mostra vazio para conta sem filial.

## 2. Transferência entre contas

**Comportamento decidido com o usuário**
- Campos: conta de origem, conta de destino, data, valor, descrição/observação.
- Gera duas pontas na **mesma data**: saída (−valor) na origem e entrada (+valor) no destino; ambas aparecem no extrato e na conciliação de cada conta como os lançamentos avulsos.
- Bloqueios (mensagens pt-BR): origem = destino; moedas diferentes; conta inativa; valor ≤ 0.
- Tarifa **não** faz parte da transferência — continua como lançamento avulso separado.
- Sem edição. Corrige-se excluindo a transferência inteira (as duas pontas juntas, numa transação) e lançando de novo. Exclusão bloqueada se qualquer ponta estiver conciliada.
- Entra como ação "Transferir" na tela de Contas Financeiras, com lista própria de transferências (consultar/excluir). Transferência entre contas de filiais diferentes é permitida.

**Decisão do designer (ajustável pelo builder)**
- Modelo: nova entidade `FinancialAccountTransfer` (Key, FromAccountCode, ToAccountCode, Date, Amount, Description, Notes, auditoria) + as duas pontas como `FinancialAccountEntry` com FK anulável `TransferKey`. Assim extrato/conciliação não mudam, e a exclusão avulsa de uma ponta fica bloqueada (só se exclui pela transferência). No extrato, a descrição das pontas indica "Transferência para/de <conta>".
- Migration aplicada em todas as empresas (padrão do projeto). Nova tela/rota exige migration de menu/permissão se a lista for item de menu próprio; preferir acesso pela tela de Contas Financeiras.
- Não há lançamento contábil (contabilidade ainda não existe).

## 3. Adiantamento sem contrato

**Inclusão**
- Contrato passa a ser opcional no diálogo de inclusão.
- Com contrato: igual hoje (contrato aprovado; parceiro, moeda, filial e direção vêm dele).
- Sem contrato: usuário informa direção (A pagar / A receber), parceiro (qualquer PN — cliente, fornecedor, prestador), filial, valor, vencimento, conta financeira, data do pagamento e observação/motivo. Moeda = moeda da conta financeira. Continua nascendo pago (baixa na conta na mesma transação).

**Uso do saldo**
- Sem contrato: abate qualquer título em aberto do **mesmo parceiro, mesma direção e mesma moeda**, com ou sem contrato.
- Pode ser vinculado a um contrato depois (reaproveitar o fluxo de vínculo); a partir daí segue as regras de contrato.
- Com contrato: continua abatendo só títulos do seu contrato (regra atual intacta).
- Devolução de saldo: sem mudança.

**Listas**
- Coluna Contrato vazia quando não houver; filtro "Com contrato / Sem contrato" nas listas de adiantamento.

**Deixado ao builder**
- Como ajustar `ApplyAdvance` e `GetAdvanceCandidates` para o caso sem contrato (o alvo hoje precisa ser Provisório com contrato — para adiantamento sem contrato, aceitar qualquer título em aberto do parceiro que o fluxo de abatimento já suporte).
- Se o vínculo posterior a contrato reutiliza `RelinkContract` (gera novo adiantamento via `AdvanceTransfer`) ou simplesmente grava a chave do contrato; preferir o que mantiver o histórico coerente com o fluxo existente.
- Guardas que assumem contrato (cancelamento/encerramento de contrato, `HasSameContractAs`) devem continuar corretos com contrato nulo.

## Decisões tomadas após a revisão (chamadas do designer, sujeitas à revisão do usuário)

1. **Alvo do abatimento sem contrato**: qualquer título do parceiro, mesma direção e moeda, com saldo em aberto, que não seja adiantamento nem esteja cancelado (Provisório, Firme ou Manual). O abatimento reduz o **saldo em aberto** via baixa de origem `AdvanceApplication` (mesmo mecanismo de hoje), nunca o valor principal.
2. **Vínculo posterior a contrato**: move só o **saldo disponível** (pago − aplicado), como o `RelinkContract` já faz; o que já foi abatido fora do contrato fica onde está. Sem saldo disponível, o vínculo é recusado.
3. **Chaves de corte e guardas de contrato**: adiantamento sem contrato não é afetado por `FirmPayablesCutoverDate`/`SalesFirmReceivablesCutoverDate`. Guardas de cancelamento/encerramento de contrato só enxergam adiantamentos vinculados ao contrato.
4. **SAPB1**: a transferência grava apenas os lançamentos locais (`FinancialAccountEntry`), como o lançamento avulso hoje; nenhuma integração com o Service Layer.
5. **Filial do adiantamento sem contrato**: obrigatória no diálogo; sugerida pela filial da conta quando houver.
6. **Permissão**: transferir e excluir transferência usam a permissão existente de Contas Financeiras; nenhuma entrada nova de menu/permissão.

## Não-objetivos

- Transferência entre moedas, transferência com data de saída ≠ entrada, tarifa embutida, edição de transferência.
- Restringir conta por filial.
- Mudar o adiantamento com contrato.

## Restrições do projeto

- Identificadores em inglês, textos ao usuário em pt-BR.
- Funciona em STANDALONE; em SAPB1 as contas/títulos seguem o comportamento atual do módulo financeiro (sem navegação OData para entidades do SAP).
- TDD em `SiagroB1.Application.Tests`; `ts-typecheck` e lint no frontend.
