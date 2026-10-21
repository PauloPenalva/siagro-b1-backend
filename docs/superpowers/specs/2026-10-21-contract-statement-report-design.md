# Extrato do Contrato (compra e venda) — conferência físico/fiscal/financeiro

Data: 2026-10-21

## Intenção

Equivalente no SiagroB1 do `AGRR062A` do Protheus ("Extrato Contrato de Venda - Analítico"), que
o usuário usa para conferir e fechar um contrato. A diferença é que aqui vale para **compra e
venda**, e o relatório separa os três lados do fechamento:

- **Físico**: o que foi entregue (romaneios e alocações).
- **Fiscal**: as notas emitidas para o contrato.
- **Financeiro**: os títulos e as baixas.

No fim aparece um quadro de saldos com as diferenças entre os três lados. Ali o usuário vê o que
ainda falta para fechar o contrato.

O usuário dispensou as perguntas de brainstorming. As decisões abaixo são minhas e estão
marcadas como tal.

## Como é emitido

- O relatório é um PDF por contrato, aberto pelo botão **"Extrato"** na tela do contrato de
  compra e na tela do contrato de venda. É o mesmo padrão do espelho de fixação, e o AGRR062A
  também é emitido a partir do contrato posicionado.
- Endpoints: `POST /reports/ContractStatement/purchase/{key}/print` e
  `POST /reports/ContractStatement/sales/{key}/print`. Os dois usam o template
  `ContractStatement.frx`.
- Não há menu nem migration: o relatório não tem tela de filtros.

## Conteúdo

### Cabeçalho

O cabeçalho repete o do AGRR062A:

- contrato, tipo (fixo ou a fixar), situação;
- parceiro, produto, safra, UM;
- período de entrega;
- quantidade, preço e valor contratados;
- frete (CIF/FOB) e condição ou vencimento.

### Seções

O relatório tem uma única fonte de linhas com uma coluna `Section`. Cada seção traz os próprios
títulos de coluna e totais.

**Sinal das quantidades e valores:** positivo é o que cumpre o contrato (entrega, nota, título no
sentido do contrato). Devolução e documento no sentido contrário entram negativos.

1. **Movimentação física**
   - Compra:
     - Uma linha por alocação do contrato (romaneio), com data, romaneio, tipo de movimento,
       placa, NF do produtor, peso bruto, descontos, peso líquido e quantidade alocada (com
       sinal).
   - Venda:
     - Uma linha por alocação (item do documento de saída), com data, NF/série, origem
       (faturamento, realocação, devolução…), placa, quantidade, peso destino, sobra/quebra e
       quantidade alocada.
     - A sobra/quebra só aparece na linha dona da diferença e com a entrega conferida. É a mesma
       regra de `ContractPositionData.SalesShortageQuery`.
   - Washouts ativos (em aprovação ou aprovado) entram como linhas "Washout" que consomem saldo.
2. **Documentos fiscais**
   - Itens de nota vinculados ao contrato: `PurchaseInvoiceItem.PurchaseContractKey` e
     `SalesInvoiceItem.SalesContractKey`.
   - Notas canceladas ficam fora. As pendentes aparecem com a situação, para conferência.
   - O valor do complemento de valor é `ComplementAmount`. O complemento de ICMS tem quantidade
     e valor de mercadoria zero e só ICMS.
   - Devoluções entram negativas.
   - A coluna ICMS se chama `fiscal*`, então some em SAPB1.
3. **Títulos financeiros**
   - Entram os documentos com a chave do contrato, menos os cancelados, os provisórios e as
     retenções. Provisórios e retenções vão só para o quadro de saldos.
   - Colunas: vencimento, código, natureza/origem, parcela, situação, valor, baixado, em aberto.
4. **Baixas**
   - As baixas dos títulos acima, com data, título, tipo de baixa, conta e valor. Estornos
     aparecem negativos.
5. **Fixações de preço**
   - Só para contrato a fixar.
   - Fixações confirmadas, com data, vencimento, volume, preço e valor.

### Quadro de saldos

O quadro é um texto monoespaçado, no estilo do AGRR062A.

- **Físico**
  - Contratado, entregue, washout e saldo físico.
  - São os números da Posição de Contratos (`ContractPositionData`), para os dois relatórios
    nunca divergirem.
- **Fiscal**
  - Quantidade faturada líquida (notas normais menos devoluções).
  - Diferença físico × fiscal.
  - Valor fiscal líquido.
- **Financeiro**
  - Títulos firmes líquidos de devoluções, sem washout e sem adiantamento.
  - Diferença fiscal × financeiro.
  - Baixado e em aberto.
  - Adiantamento não compensado.
  - Saldo líquido a pagar/receber.
  - Saldo provisório a faturar.
  - Retenções.

## Fora do escopo

- Relatório de vários contratos de uma vez.
- Exportação para Excel.
- Câmbio ou outra moeda: tudo é exibido na moeda do documento, sem conversão.

## SAPB1

Parceiro, produto e local vêm dos snapshots do contrato. Os tributos ficam ocultos
(`pStandalone=false`). Se o financeiro não estiver em uso, as seções financeiras saem vazias.
