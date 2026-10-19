# Faturamento da fatura de armazenagem (NF-e + NFS) e financeiro pela natureza

Data: 2026-10-19 · Status: desenho aprovado em conversa, aguardando revisão do documento

## Intenção e porquê

Hoje a fatura de serviços de armazenagem (`StorageInvoice`) é enviada ao cliente e, quando ele
paga, o usuário a "encerra" — o encerramento faz as vezes de baixa. Não há documento fiscal nem
título a receber ligados a ela.

O cliente quer que a fatura seja **faturada por um ou mais documentos de saída**: parte do valor
sai em **NF-e** (beneficiamento) e parte em **NFS** da prefeitura (armazenagem). A divisão de
valores ainda não é conhecida, por isso o usuário digita quanto vai em cada documento. O
financeiro passa a nascer dos documentos de saída (pela natureza) e a tela da fatura mostra os
títulos criados e sua situação.

Junto vem uma correção de regra geral: hoje o documento de saída só gera título quando a linha
tem contrato de venda. Isso está errado — vendas de ativo e de sacaria, por exemplo, não têm
contrato e precisam de título.

## Partes (ordem de implementação)

### 1. Financeiro pela natureza, com ou sem contrato (vale para todo documento de saída)

- Regra nova: se a natureza (`Usage.CreatesFinancialDocument`) da linha estiver marcada, o
  documento de saída confirmado gera título a receber firme, **com ou sem contrato** na linha.
- Linha com contrato: comportamento atual mantido (título ligado ao contrato, rateio pelo
  ledger `SalesContractAllocation`).
- Linha sem contrato: título sem `SalesContractKey`, valor = `GrandTotal` da linha (agregado por
  documento conforme o modelo atual de parcelas).
- `RequiresContract` da natureza continua só validando se o contrato é obrigatório; não decide
  mais se há financeiro.
- Vencimento/parcelas: da **condição de pagamento do documento de saída**, que vem carregada da
  condição padrão do PN (já existente no cadastro).
- Ponto de partida no código: `FinancialDocumentsSalesFirmSyncService.DesiredAsync` (hoje exige
  `SalesContractKey`), chamado por `SalesInvoicesConfirmService`. O lado de compra já tem o
  caso sem contrato (`FinancialDocumentOrigin.OtherPurchaseInvoice`) — usar como modelo.
- Cancelamento/estorno do documento cancela os títulos ainda não baixados, como hoje.
- Exclusões atuais mantidas: complemento de ICMS, custódia, documento de liberação de entrega.

### 2. NFS como documento de saída

- A NFS é um **documento de saída comum** (mesma tela, natureza, condição de pagamento, título),
  marcado como NFS.
- Não há integração com prefeitura: o usuário digita número, série e data de emissão da NFS já
  emitida no portal. Nada de transmissão, DANFE, XML, e-mail de NF-e ou numeração automática da
  NF-e para esse documento.
- Confirmar a NFS gera o financeiro pela regra da parte 1.
- A NFS não movimenta estoque fiscal (é serviço) — garantido pela natureza usada.

### 3. Faturar a fatura de armazenagem

- Produtos: existirão dois itens de serviço cadastrados pelo cliente —
  **SERVIÇO DE BENEFICIAMENTO** (sai em NF-e) e **SERVIÇO DE ARMAZENAGEM** (sai em NFS). Valores
  digitados pelo usuário.
- A partir da fatura, ação "Faturar": o usuário cria um documento de saída (NF-e ou NFS) para o PN
  da fatura com o valor que quiser; pode repetir até cobrir o total.
- O documento de saída guarda a referência à fatura de origem.
- Situações novas da fatura (substituem o fluxo atual Aberta/Encerrada como "baixa"):
  - **Aberta** — nada faturado;
  - **Faturada parcial** — soma dos documentos confirmados < total da fatura;
  - **Faturada** — soma = total;
  - **Paga** — faturada e todos os títulos a receber baixados;
  - **Cancelada** — mantida.
- Tela da fatura (Detail): seção com os documentos de saída que a faturaram (tipo NF-e/NFS,
  número, valor, situação) e os títulos a receber gerados (vencimento, valor, saldo, situação).

## Restrições decididas pelo usuário

- **Só daqui para frente:** a regra da parte 1 não cria títulos retroativos para documentos de
  saída já confirmados sem contrato. Aplica-se a documentos confirmados após a implantação
  (data de corte por configuração, no mesmo estilo de `SalesFirmReceivablesCutoverDate`; a
  forma exata fica com o implementador).
- **Retenções na NFS:** poderão existir conforme a configuração da natureza, mas **hoje não há
  retenção**. Não implementar retenção de ISS/PIS/COFINS/IR/CSLL agora; o título é o valor
  bruto da linha. Não fechar o caminho para descontar retenções da natureza no futuro.
- **Modos:** deve funcionar em **STANDALONE e SAPB1**. O implementador verifica como documentos
  de saída e títulos se comportam hoje no modo SAPB1 (ver memória "Faturamento travado em
  SAPB1") e garante o vínculo fatura → documento e a situação "Paga" nos dois modos.

## Decisões minhas (o usuário pode revisar)

- Documento de saída em rascunho (não confirmado, não cancelado) já consome o saldo da fatura,
  para evitar faturamento em dobro; a checagem roda ao criar/alterar o documento.
- "Faturar" abre o documento de saída pré-preenchido (PN, condição de pagamento do PN, referência
  da fatura); o usuário escolhe NF-e (SERVIÇO DE BENEFICIAMENTO) ou NFS (SERVIÇO DE ARMAZENAGEM),
  a natureza e digita o valor. O par item/natureza não é configurado em tela nova.
- Depois do primeiro documento de saída vinculado, a fatura fica travada: o cálculo diário e a
  edição manual não alteram mais seu total.
- A ação "Encerrar" deixa de existir; a situação passa a ser derivada de documentos e títulos.
- Cancelar a fatura exige que todos os seus documentos de saída estejam cancelados.
- "Paga" = faturada e todos os títulos a receber não cancelados com saldo zero.
- NFS duplicada é barrada por PN + série + número (como a trava da NF-e).

- A soma dos documentos de saída (não cancelados) não pode ultrapassar o total da fatura; a tela
  mostra o saldo a faturar.
- Cancelar/estornar um documento devolve o valor ao saldo da fatura e recalcula a situação.
- Linhas de quebra técnica/qualidade (`TechnicalLoss`, `QualityLoss`) continuam informativas; o
  valor faturável é o `TotalAmount` da fatura.
- Situação da fatura é recalculada ao confirmar/cancelar documento e ao baixar/estornar título
  (padrão de recálculo já usado em `SalesShipmentReleasesRecalculateShippedService`).
- Faturas já encerradas antes da implantação: tratadas como **Paga** (legado), sem documentos.

## Não-objetivos

- Emissão de NFS-e direto na prefeitura (pode vir depois).
- Divisão automática do valor entre NF-e e NFS.
- Mudar como a fatura é calculada (períodos, serviços, quebras).

## Anti-objetivos

- Documento de saída de natureza com financeiro e sem contrato **sem** título.
- NFS tentando transmitir à SEFAZ ou consumir a numeração da NF-e.
- Fatura faturada além do total.

## Deixado ao implementador

- Forma de marcar NFS no documento de saída (novo `InvoiceType` ou flag), desde que filtrável na
  lista de saídas.
- Como o link fatura → documento é guardado (chave no cabeçalho do documento de saída é o
  caminho mais simples).
- Nova origem em `FinancialDocumentOrigin` se necessária para o caso sem contrato.
- Migrations (lembrar: rodar em todas as empresas) e menu/permissões, se houver ação nova.
