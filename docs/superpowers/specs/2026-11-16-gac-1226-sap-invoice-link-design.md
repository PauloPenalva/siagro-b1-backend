# GAC-1226 — Vincular NF do SAP à carga (somente `Erp=SAPB1`)

Data: 2026-11-16 · Aprovado pelo usuário em chat (16/11/2026)

## Por quê

A Yokotobi emite as A/R Invoices (NF-e) no SAP Business One e hoje refaz o faturamento no Siagro:
"Faturar" na carga gera o documento de saída (`SalesInvoice`) e, depois, alguém digita número, série
e chave em "Informar Nota Fiscal". O chamado pede que o Siagro **consuma a invoice emitida no SAP**
no lugar desse faturamento.

O documento de saída do Siagro **continua existindo**: alocação do contrato de venda, saldo da
liberação, títulos firmes, quantidade faturada/status da carga, conferência de entrega, ticket de
descarga e recusa dependem dele. Muda só de onde ele nasce.

## Objetivos, não-objetivos, anti-objetivos

- **Objetivo:** no detalhe da carga, escolher uma A/R Invoice do SAP ainda não vinculada e gerar o
  documento de saída a partir dela, com os mesmos efeitos do faturamento atual.
- **Objetivo:** refletir no Siagro o cancelamento feito no SAP.
- **Não-objetivo:** criar a invoice no SAP a partir do Siagro (Service Layer) — fase posterior.
- **Não-objetivo:** devolução no SAP (nota de crédito, ORIN) e documento avulso (`POST
  /SalesInvoices`) digitado em SAPB1.
- **Anti-objetivo:** qualquer mudança de comportamento em `Erp=STANDALONE` além de ocultar o botão
  "Informar Nota Fiscal". Nada é gravado no SAP nem no banco do add-on fiscal.

## Abordagens consideradas

1. **A — Siagro lê a OINV (escolhida).** Só leitura, segue o padrão do `SapErpDbContext`, e tudo o
   que cria (vínculo por `DocEntry`, leitura de NF e de cancelamento) é pré-requisito de B.
2. **B — Siagro cria a invoice no SAP pela Service Layer.** Digitação única, mas exige impostos,
   utilização, série, estoque, reenvio/idempotência e ainda precisa ler a NF de volta. Maior custo e
   risco; fica para depois.
3. **Casamento por campo de usuário na OINV + importação automática.** Descartado nesta fase: exige
   criar campo no SAP e disciplina de preenchimento. O usuário preferiu escolher na carga.

Sobre a chave da NF-e: a Service Layer **não** a devolve nas notas de grãos (testado em produção em
16/11/2026 — só espelha `OINV.U_ChaveAcesso`, que está nulo nelas). A chave vem do banco do add-on
fiscal, lido direto.

## Decisões do usuário

- Só existe em `Erp=SAPB1`. Em STANDALONE, **ocultar "Informar Nota Fiscal"**.
- O usuário **escolhe a invoice dentro da carga** (ação "Vincular NF do SAP").
- O "Faturar" manual continua, mas em SAPB1 gera **apenas tipo Outros** (`TaxDocumentKind.Other`) —
  são as operações hoje marcadas como "Sem NF". No SAP só nascem as do tipo NF-e.
- A quantidade da invoice deve ser **igual** ao saldo disponível da carga (tolerância 0,001).
- Invoice cancelada no SAP → **cancelamento automático** do documento de saída por job; se uma trava
  do cancelamento impedir, fica uma pendência visível.
- Chave e status da NF-e vêm do add-on fiscal do cliente. Na Yokotobi é o **InvOne**: banco
  `DBInvOne`, tabela `Process`. A consulta fica **separada por add-on** (provedor plugável).

## Fatos verificados (produção Yokotobi, somente leitura, 16/11/2026)

- `OINV`: `DocEntry`, `DocNum`, `DocDate`, `CardCode`, `BPLId`, `CANCELED` (`N` normal, `Y` original
  cancelada, `C` documento de cancelamento), `Serial` (nº da NF, int), `SeriesStr`, `Model` (FK
  `ONFM.AbsEntry`; 39 = `NfmCode` 55, NF-e).
- `INV1`: nas notas de grãos sempre **1 linha**, em KG — `ItemCode`, `Quantity`, `unitMsr`, `Price`,
  `LineTotal`, `WhsCode`, `Usage`, `CFOPCode`. Transportadora em `INV12.Carrier`.
- Itens integrados: `OITM.U_YKT_Integra_Siagro = 'Sim'`. Cerca de 1.400 invoices/mês; 863
  cancelamentos entre janeiro e novembro.
- `DBInvOne.dbo.Process`: `CompanyId`, `DocType` (13 = A/R Invoice), `DocEntry`, `KeyNfe`,
  `StatusId` (4 = "Autorizado", pela view `YJ_VW_NFE_STATUS`), `IsCanceled`, `ProtocolNumber`,
  `DataDeAutorizacao`. O login `idx` (o mesmo do `SapDB`) lê esse banco.
- O SQL Server do cliente interpreta data como dia/mês: datas **sempre como parâmetro**. É SQL
  Server 2016.

## Regras

### Listagem das invoices vinculáveis (por carga)

Aparecem as A/R Invoices que atendem a tudo:

- `CANCELED = 'N'`, modelo NF-e, `Serial` preenchido;
- exatamente uma linha, com `ItemCode` igual ao item da carga;
- `Quantity` igual ao saldo disponível da carga (±0,001);
- `DocDate` dentro do período informado (padrão: últimos 15 dias);
- filial da invoice igual à filial da carga;
- quando há provedor de add-on configurado: NF-e **autorizada** e não cancelada no add-on;
- ainda não vinculada a um documento de saída ativo (ver unicidade abaixo).

O cliente **não** é filtrado pelo `ShipmentLoad.CardCode` (campo declarado como informativo na
entidade). A lista mostra cliente, data, número/série, quantidade, preço, total e transportadora.

### Vínculo

Entrada: carga, `SapDocEntry`, liberação de venda escolhida. Tudo é relido e revalidado no servidor:

1. Modo SAPB1, senão recusa.
2. A invoice continua atendendo às condições da listagem.
3. Não está vinculada a outro documento ativo: *"A NF {n} do SAP já está vinculada ao documento de
   saída {x}."*
4. A liberação está ativa, é do item da carga, e o **cliente do contrato da liberação é o
   `OINV.CardCode`**.
5. Quantidade igual ao saldo: *"A quantidade da NF do SAP ({q}) é diferente do saldo da carga
   {código} ({a}). O vínculo exige quantidades iguais."*
6. Número+série não duplicados na filial (mesma trava do "Informar Nota Fiscal").

O documento de saída nasce **Confirmado**, pelo mesmo orquestrador do faturamento da carga
(`ShipmentBillingCreateSalesInvoiceService`), com:

| Campo | Origem |
|---|---|
| Data, cliente, quantidade, preço unitário, CFOP | invoice do SAP |
| Número (9 dígitos, zeros à esquerda), série (3 dígitos) | `OINV.Serial`, `OINV.SeriesStr` |
| Chave | provedor do add-on (pode ficar nula e ser preenchida depois pelo job) |
| Tipo de frete (CIF/FOB) | incoterm da invoice do SAP |
| Local de entrega | local de entrega da liberação escolhida (`DeliveryLocationCode`) |
| Frete por tonelada | o informado na carga |
| Transportadora, caminhão, motorista, carga | carga |
| Contrato, liberação, unidade de medida | liberação escolhida |
| `TaxDocumentKind` | `Nfe`; `NfeStatus` permanece `None` (os campos de emissão própria não se aplicam; `Authorized` travaria o cancelamento) |
| Natureza | como no faturamento da carga hoje (padrão ou nula) |
| `SapDocEntry`, `SapDocNum` | invoice do SAP |

Efeitos: os mesmos do faturamento atual, sem duplicar código — alocação no contrato de venda,
recálculo da liberação, títulos firmes, recálculo da carga, log de movimento "Faturado".

Transportadora da invoice diferente da carga: apenas aviso na lista, não bloqueia.

### Documento vinculado

- A ação de informar nota fiscal é recusada no servidor: *"Número, série e chave deste documento vêm
  da nota fiscal do SAP."*
- Quantidade, preço e item da linha ficam travados para edição.
- **Vínculo errado se desfaz.** O usuário pode ter escolhido a NF errada, então o documento
  vinculado aceita os caminhos normais: **estornar a confirmação** (volta a Pendente, devolve saldo
  ao contrato; continua reservando a NF e contando no saldo da carga, como todo Pendente),
  **excluir** o Pendente (libera a NF e o saldo da carga) e **cancelar**. Depois disso a NF volta a
  aparecer em "Vincular NF do SAP". Reconfirmar um Pendente vinculado revalida a invoice no SAP
  (não cancelada).
- **Unicidade:** um `SapDocEntry` só pode estar em um documento ativo. Documentos `Cancelled` e
  `Returned` liberam a NF para novo vínculo (mesma regra do GAC-1194).

### Faturar manual em SAPB1

O orquestrador força `TaxDocumentKind = Other` quando não há vínculo SAP (coerção, não recusa —
frontends em cache ainda mandam `Nfe`). Vale para o "Faturar" da carga e para `/shipment-billing`.
O documento nasce com `WithoutTaxDocument = true` ("Sem NF") e não aceita número/série/chave: em
SAPB1 a NF só entra pelo vínculo.

### "Informar Nota Fiscal"

O botão fica **oculto nos dois modos** (decisão do usuário em 16/11/2026: em STANDALONE a NF-e é do
próprio Siagro; em SAPB1 a NF vem do vínculo e o manual nasce "Sem NF"). A ação
`SalesInvoicesSetDocumentNumber` permanece no backend, sem entrada na tela; documentos antigos da
Yokotobi (NF digitada, sem `SapDocEntry`) ficam como estão.

### Cancelamento vindo do SAP (job `sap-invoice-sync`)

Só em SAPB1, por empresa, no **intervalo configurado pelo usuário** na tela "Integração SAP"
(`SapIntegrationSettings`: `InvoiceSyncEnabled`, padrão ligado; `InvoiceSyncIntervalMinutes`, padrão
15, de 1 a 1440; `InvoiceSyncLastRunAt`, só leitura). O Hangfire dispara a cada minuto e o serviço só
executa quando o intervalo da empresa já venceu; desligado, não executa. A tela mostra a última
execução e tem o botão "Sincronizar agora". Em cada execução:

- Para cada documento vinculado ativo cuja invoice está `CANCELED = 'Y'` no SAP (ou cancelada no
  add-on), chama `SalesInvoicesCancelService` com o usuário `sap-sync`.
- Se uma trava do cancelamento barrar (título baixado, ticket de descarga, devolução existente
  etc.), grava `SapCancelPending = true`, o motivo e a data da detecção; o documento fica intacto e
  a tentativa se repete na rodada seguinte. Cancelar com sucesso (pelo job ou à mão) limpa a
  pendência.
- O mesmo job preenche a chave de documentos vinculados que nasceram sem ela.
- SAP ou add-on fora do ar não derruba o job nem as outras empresas.

### Provedor do add-on fiscal

Interface `ISapFiscalDocumentSource`: dado um conjunto de `DocEntry` de A/R Invoice, devolve por
documento chave, autorizado (sim/não), cancelado (sim/não), protocolo. Implementações:

- `InvOneFiscalDocumentSource` — contexto EF próprio (`InvOneDbContext`), connection string
  `ConnectionStrings:SapFiscalAddonDB`, tabela `Process` com `DocType = 13`.
- `NullSapFiscalDocumentSource` — sem add-on: não filtra por autorização e a chave fica nula.

Seleção por `SAPB1:FiscalAddon` (`InvOne` ou vazio), registrada só no ramo SAPB1. Um add-on novo
(TaxOne, SKILL…) é uma implementação nova, sem tocar no restante.

## Dados novos

`SALES_INVOICES` (migration `AddSalesInvoiceSapLink`): `SapDocEntry int null`, `SapDocNum int null`,
`SapCancelPending bit`, `SapCancelPendingReason varchar(500) null`, `SapCancelDetectedAt datetime
null`. Índice único filtrado em `SapDocEntry` (`IS NOT NULL` e status diferente de Cancelled e de
Returned). Esses campos nunca são aceitos do cliente no create/patch.

Tabela nova `SAP_INTEGRATION_SETTINGS` (uma linha por empresa), na mesma migration, e o item de menu
"Integração SAP" (visível só em SAPB1) por migration de menu.

## Tela

- **Integração SAP** (nova, padrão das telas de configuração como `emailSettings`): liga/desliga a
  sincronização, intervalo em minutos, última execução e "Sincronizar agora".

- **Detalhe da carga:** botão "Vincular NF do SAP" ao lado de "Faturar", com a mesma condição de
  status/saldo, só em SAPB1 (flag iniciada `false`). Abre um diálogo com o período, a tabela de
  invoices do SAP e a escolha da liberação (pré-selecionada quando só há uma). Ao concluir, navega
  para o documento de saída. Na tabela de documentos da carga: coluna "Doc. SAP" e indicador
  "Cancelamento SAP pendente" com o motivo.
- **Diálogo de faturar:** em SAPB1 envia `Other` e mostra o aviso *"Com integração SAP, o Faturar
  gera documento sem NF-e. Para nota fiscal use Vincular NF do SAP."*
- **Lista de documentos de saída:** botão "Informar Nota Fiscal" removido da tela; coluna "Doc. SAP"
  e o mesmo indicador de pendência.

## Escolhas do autor (não decididas pelo usuário)

- Conexão com o SAP e com o add-on é **uma por instalação** (`SapDB`, `SapFiscalAddonDB`), como o
  `SapErpDbContext` já é hoje; não há SAP por empresa nesta fase.
- Só NF-e autorizada aparece para vincular; transportadora divergente só avisa; quantidade, preço e
  item travados no documento vinculado.
- O job trata também documento vinculado **Pendente** cuja invoice foi cancelada no SAP.

## Confirmado em produção (consulta somente leitura, 16/11/2026)

- **Filial:** `OINV.BPLId` é o próprio `Branch.Code` do Siagro (3, 6, 7, 8), comparado como texto.
  `Process.CompanyId` do InvOne também é o `BPLId`.
- **InvOne:** `KeyNfe` vem com 44 dígitos puros; `StatusId` 4 = Autorizado, 10 = Cancelado (5 e 25
  rejeitado/denegado, 12 erro, 17 inutilizado); `IsCanceled` é bit **anulável** (nulo = não).
  Cancelado no add-on = `StatusId == 10` ou `IsCanceled == true`.
- **Número e série** já gravados em `SALES_INVOICES`: `000198105` / `001` — o vínculo grava igual.
- **Tipos:** `OINV.Model` nvarchar(6) (valor `'39'`), `Serial` int, `SeriesStr` nvarchar(3),
  `CANCELED` char(1), `ShipToCode` nvarchar(50); `INV1.Usage` int, `CFOPCode` nvarchar(6),
  `Quantity`/`Price`/`LineTotal` numeric(19,6), `unitMsr` nvarchar(100); `INV12.Carrier`
  nvarchar(15), `Incoterms` nvarchar(3).
- **Tipo de frete:** `INV12.Incoterms` é o modFrete da NF-e — `0` → `Cif`, `1` → `Fob`, `2` → `Ter`,
  `9` → `None`. Outro valor ou vazio: a invoice não é vinculável.
- **Local de entrega:** no Siagro é um código de parceiro (`DeliveryCardCode`, às vezes diferente do
  cliente); a invoice do SAP só traz o nome do endereço (`ShipToCode`, ex.: "FATURAMENTO"), que não
  identifica parceiro. Escolha do autor: `DeliveryCardCode` = `DeliveryLocationCode` da liberação de
  venda escolhida (cadastro que já vem do SAP). `ShipToCode` não é mapeado.
- **Frete por tonelada:** `ShipmentLoad.FreightPrice` → `SalesInvoice.FreightCostStandard`
  (conferir a escala entre os dois campos na implementação; nulo vira 0).

## Deixado ao construtor

Nomes de classes/arquivos, forma do DTO da listagem, paginação/limite da lista, texto fino das
mensagens, organização dos testes.

## Verificação

Testes de aplicação (TDD) para cada regra acima, incluindo um teste de tradução SQL
(`ToQueryString` no provider SqlServer) para as consultas ao SAP. Ponta a ponta com a stack local no
perfil `yktb` contra **cópia** do banco do Siagro (SAP e InvOne só são lidos): vincular uma NF real a
uma carga de teste, conferir contrato, liberação, título e status da carga, e simular o
cancelamento. Em STANDALONE (perfil `ceagui`): botão oculto e faturamento inalterado.

Implantação: migration antes do frontend; `SapFiscalAddonDB` e `SAPB1:FiscalAddon=InvOne` na
configuração da Yokotobi.
