# Ordem de Frete + Fatura de Frete (módulo TMS) — design

Data: 2026-10-24 · Repos: `siagro-b1-backend` + `siagro-b1-frontend`

## Intenção

A Tagui é dona da transportadora **Univia**, que vai operar no mesmo SiagroB1 como **outra empresa
no login** (banco próprio). A Univia vende frete para a própria Tagui (tratada como cliente comum)
e para terceiros (trigo, açúcar, fertilizantes, cal...). Ela precisa do mínimo de um TMS de
mercado (referências: Protheus TMS/GFE, Senior, ESL, Bsoft):

1. registrar **o que foi combinado com o cliente** antes das viagens (Ordem de Frete);
2. emitir os CT-e **a partir** dessa ordem, consumindo saldo;
3. **cobrar** os CT-e agrupados numa fatura com vencimento único.

O usuário não tem experiência em transportes: o sistema deve seguir o padrão de mercado e ser
o mais enxuto possível.

## O que já existe (base)

- CT-e (`TransportDocument`) completo: emissão, cancelamento, CC-e, averbação, MDF-e, inutilização.
- Ao autorizar, cada CT-e gera **um título a receber próprio**
  (`FinancialDocumentsTransportService`, origem `TransportDocument`, parcelado pela condição de
  pagamento do CT-e, só se a natureza "gera financeiro").
- Cálculo de frete Fixo × Saco (`FreightPriceCalculator`), cadastros de transporte, numeração
  Automático/Manual com séries, financeiro (títulos, baixas, travas de cancelamento), FastReport.

## Objetivos

### 1. Ordem de Frete (`FreightOrder`)
- Campos: filial, número (série própria — modelo Numeração Automático/Manual), data, **tomador**
  (cliente), remetente, destinatário, município de origem e de destino, produto (item),
  **volume contratado** (kg; exibir t/sacos), **método de preço** (por tonelada, por saco, fixo
  por viagem — estender `FreightCalculationMethod` se preciso) e preço unitário, vigência
  (início/fim), condição de pagamento, natureza (usage) padrão do CT-e, observação.
- Situação: **Aberta → Em execução → Concluída / Cancelada**.
  - Em execução: automática no 1º CT-e autorizado.
  - Concluída: manual (encerrar), permitida com saldo; ou automática quando saldo ≤ 0.
  - Cancelada: só sem CT-e autorizado vinculado.
- **Saldo** = volume contratado − Σ peso dos CT-e autorizados vinculados (cancelados devolvem).
  Mostrar volume transportado, saldo e valor faturado/a faturar.
- Exceder o saldo: **avisa, não bloqueia** (frete real costuma variar alguns kg).
- Telas: lista (FilterBar: tomador, situação, período, produto) + ObjectPage (dados + aba com os
  CT-e vinculados + aba Comentários se barato reaproveitar).

### 2. CT-e a partir da ordem
- `TransportDocument.FreightOrderKey` (opcional — CT-e avulso continua possível).
- Botão "Emitir CT-e" na ordem: cria o rascunho com tomador, partes, rota, produto, natureza,
  condição de pagamento e preço preenchidos; o operador informa veículo, motorista, NF-e da carga
  e peso; valor do serviço = `FreightPriceCalculator` (editável).
- Também permitir vincular a ordem ao editar um rascunho de CT-e (value help filtrado por
  tomador e situação aberta/em execução).

### 3. Fatura de Frete (`FreightInvoice`)
- Cabeçalho: filial, número/série próprios, tomador, emissão, vencimento (pela condição de
  pagamento ou informado), desconto, acréscimo, total, observação.
- Itens (`FreightInvoiceItem`): CT-e **autorizados** do mesmo tomador, ainda **sem fatura**, cuja
  natureza gera financeiro e cujo título **não tem baixa**. Um CT-e só em uma fatura ativa.
  Pode juntar CT-e de várias ordens.
- Situação: **Aberta (rascunho) → Emitida → Liquidada / Cancelada**.
- **Emitir**: baixa os títulos dos CT-e por **aglutinação** (baixa sem movimento de conta, tipo
  fatura do FINA280 do Protheus) e gera **um título a receber** da fatura (origem nova
  `FreightInvoice`), parcelado pela condição de pagamento.
- **Liquidada**: derivada — título(s) da fatura totalmente baixados.
- **Cancelar emitida**: só se o título da fatura não tem baixa; exclui/cancela o título da fatura
  e estorna as baixas por aglutinação (títulos dos CT-e voltam a abertos).
- Telas: lista de faturas; "Gerar fatura" (filtra tomador + período, lista CT-e pendentes com
  check, mostra total, confirma); **geração em lote** opcional: uma fatura por tomador para
  todos os pendentes do período.
- Impresso FastReport: dados do emitente/tomador, vencimento, total, relação dos CT-e (número,
  chave, emissão, origem/destino, peso, valor) e dados bancários da filial.

### 4. Travas
- CT-e em fatura ativa: bloquear cancelamento e CC-e que altere valor — mensagem "CT-e está na
  fatura X; retire-o ou cancele a fatura antes".
- Título de CT-e baixado por aglutinação não pode ser estornado/editado isoladamente.
- Ordem com CT-e autorizado não pode ser excluída nem ter tomador/produto alterados.

### 5. Menu
- Itens novos sob o menu TMS existente: "Ordens de Frete" e "Faturas de Frete" (migration de
  menu; ícone `sap-icon://folder-blank` só se for módulo superior novo).

## Não-objetivos (agora)
Cotação/proposta comercial; tabela de frete por cliente/rota; programação de frota/veículos;
pagamento de frete a agregado/terceiro (contas a pagar, carta-frete); conferência de fatura de
frete recebida (GFE); boleto registrado/CNAB; integração automática Univia → entrada na Tagui.

## Anti-objetivos
- Obrigar ordem para emitir CT-e (quebraria o fluxo atual da CEAGUI/Tagui).
- Duplicar receita: a fatura **não** gera receita nova nem lançamento contábil de receita.

## Restrições
- Empresas no login = bancos separados; nada cruza bancos.
- Convenções do projeto: identificadores em inglês, telas em pt-BR, migrations App/Common
  conforme o padrão, TDD em `SiagroB1.Application.Tests`, `[NotMapped]`/OData armadilhas
  conhecidas.
- A Univia roda em **STANDALONE** (confirmado pelo usuário). Não há escopo para SAPB1: em modo
  SAPB1 os menus/serviços novos ficam ocultos/indisponíveis.

## Decisões minhas (o usuário pode reverter)
- Manter título por CT-e e aglutinar na fatura (em vez de flag "não gera título").
- Aglutinação não gera lançamento contábil próprio (receita já contabilizada no CT-e); se a
  contabilidade on-line exigir lançamento de transferência entre títulos, usar o padrão do
  módulo (builder decide).
- Exceder saldo da ordem só avisa.
- Volume da ordem em kg.

## Respostas à revisão do builder (decisões minhas)
- **Desconto/acréscimo saem da fatura**: total da fatura = Σ títulos dos CT-e. Desconto/juros
  ficam na baixa do título da fatura (fluxo de baixa já existente). Remove os campos do cabeçalho.
- **Contabilidade da aglutinação**: nenhum lançamento novo (mesmo cliente, mesma conta de
  clientes). O builder confirma se a baixa sem conta dispara LP/reconciliação; se disparar, a
  baixa de origem "aglutinação" é ignorada por esses hooks.
- **Peso do saldo**: peso bruto da carga do CT-e (a quantidade "PESO BRUTO" em kg). No preço
  fixo por viagem, o volume continua em kg (o fixo só muda o valor do CT-e).
- **Situação da ordem**: Em execução/Aberta/Concluída-automática são recalculadas a cada
  autorização/cancelamento de CT-e (reabre se o saldo voltar). Encerramento **manual** é final.
  Ordem Concluída não aceita CT-e novo.
- **Parcelas**: o CT-e entra com todas as parcelas, nenhuma baixada. A condição de pagamento da
  fatura pode diferir da do CT-e (padrão: a do tomador/ordem).

## Deixado ao builder
Nomes de tabelas/serviços, layout exato das telas, forma da baixa por aglutinação dentro do
modelo `FinancialSettlement` existente, fatiamento em entregas (sugestão: A = Ordem + CT-e da
ordem; B = Fatura + financeiro + impresso).
