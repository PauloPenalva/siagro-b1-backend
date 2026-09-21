---
status: desenho aprovado 21/09/2026 — não implementado
origem: migração Tagui → SiagroB1 (bloco 4 do estudo de viabilidade); necessidade também da Yokotobi
---

# Design — Minutas de contrato, modelos, signatários e assinatura eletrônica (D4Sign)

## Contexto

O SiagroB1 registra o contrato de compra/venda mas não produz o **documento** do contrato. Hoje o
que existe é `SignatureStatus` no cabeçalho (Aguardando Assinatura / Assinado / Sem Contrato),
preenchido à mão pela action `PurchaseContractsSetSignatureStatus` /
`SalesContractsSetSignatureStatus`, e o pré-contrato de compra da Yokotobi
(`PrePurchaseContractReportService`), que só imprime um PDF fixo em modo SAPB1 para o jurídico
redigir o contrato fora do sistema.

O Siagro Tagui (Java) tem o ciclo completo: **modelo de contrato** (HTML com placeholders editado
pelo usuário) → **minuta** (HTML renderizado com os dados do contrato, PDF sob demanda) → envio
para **assinatura eletrônica** (D4Sign ou DocuSign) → **webhook** atualiza a situação. É o
primeiro bloco a ser portado na migração da Tagui para o SiagroB1, e entra como feature do
produto porque a Yokotobi também precisa.

O que foi aprendido lendo o Java e que muda o desenho (detalhes em
`C:\Projetos\Tagui\Siagro\Backend`, pacotes `domain/contratocompraminuta`, `domain/modelocontrato`,
`infrastructure/d4sign`):

- "Cláusulas" não existem mais lá: a tabela `modelo_contrato_clausula` foi removida no release
  7.8; o modelo é **um único texto HTML** com placeholders Velocity (`$fornecedor_cnpj`).
- A minuta guarda o **HTML já renderizado** (snapshot) e gera o PDF na hora do download e do
  envio (iText `HtmlConverter`). O `.docx` exportado é HTML cru dentro de um parágrafo — inútil.
- Signatários vêm de dois cadastros: os da **própria empresa** (por filial) e os do
  **parceiro** (`participante_signatarios`).
- O webhook do D4Sign é `POST multipart/form-data` com `uuid`, `type_post`, `message`, `email`,
  **sem HMAC nem segredo**; a rota Java é `permitAll` e confia no payload.
- Defeitos conhecidos que não se repetem aqui: reenvio sobrescreve o id externo; venda nunca
  recebe webhook; cancelamento tem semântica diferente por driver; PDF assinado nunca é baixado;
  nenhuma propagação para o contrato; sem status por signatário.

## Decisões do usuário (21/09/2026)

1. **Só D4Sign** na primeira versão. A abstração de provedor existe (`IESignatureProvider`) para
   o driver da Yokotobi entrar depois, mas não se implementa DocuSign agora.
2. **Usuário final edita o modelo na tela** (editor rich-text UI5 + lista de placeholders). Sem
   modelos fixos em código e sem upload de DOCX.
3. **Compra e venda** desde a primeira versão, com uma única estrutura de minuta.
4. **PDF por Chromium headless (PuppeteerSharp)**: fidelidade total ao que o usuário vê no
   editor. Custo aceito: Chromium (~150 MB) ao lado do `SiagroB1.Web` em cada servidor.
5. Envio ao D4Sign **síncrono** na action (o usuário espera a resposta), com job de
   reconciliação periódico cobrindo webhook perdido — e não outbox como o WhatsApp, porque envio
   de assinatura é ato do usuário com erro a mostrar, não notificação.
6. **Uma tabela de minutas** para os dois tipos de contrato, com duas FKs anuláveis e check
   constraint, em vez de duplicar serviços como o Java fez.

## Fora de escopo

- DocuSign, Clicksign ou qualquer outro provedor (entra como novo `IESignatureProvider`).
- Tipo de minuta **Cessão de Crédito**: o conceito não existe no SiagroB1.
- Exportar `.docx`.
- Gerar a minuta automaticamente na aprovação do contrato. O gancho existe
  (`PurchaseContractsApprovalService` enfileira efeitos antes do `SaveChanges`), mas a Tagui
  cria a minuta manualmente e é assim que fica.
- Migração dos modelos e minutas históricas da Tagui (Postgres → SQL Server): é parte do bloco
  9 do estudo de viabilidade. Este desenho só garante que a migração seja possível (placeholders
  com os mesmos nomes; ver "Placeholders").
- Assinatura presencial, certificado ICP-Brasil, autenticação por selfie ou SMS do D4Sign: todos
  os flags de signatário ficam no padrão (e-mail), como na Tagui.

## Desenho

### Modelo

Quatro tabelas novas em `AppDbContext`, uma coluna nova em `ContractChangeLogFields`.

**`CONTRACT_TEMPLATES`** (`ContractTemplate : BaseEntity`) — modelo de contrato.

| Coluna | Tipo | Nota |
|---|---|---|
| `Name` | `VARCHAR(100) NOT NULL`, índice único | nome interno que aparece na lista de escolha |
| `Title` | `VARCHAR(200) NOT NULL` | título do documento (nome do arquivo no D4Sign) |
| `ContractType` | `int NOT NULL` (`ContractTemplateScope`: `Purchase=0`, `Sales=1`, `Both=2`) | filtra quais modelos aparecem em cada contrato |
| `BodyHtml` | `VARCHAR(MAX) NOT NULL` | HTML com `{{placeholders}}` |
| `Active` | `bit NOT NULL default 1` | inativo não aparece para criar minuta, mas minutas antigas continuam apontando para ele |

Sem `BranchCode`: modelo é documentação da empresa, não documento de filial. Não usa `DocNumber`.

**`COMPANY_SIGNATORIES`** (`CompanySignatory : BaseEntity`) — quem assina pela empresa.

| Coluna | Tipo | Nota |
|---|---|---|
| `BranchCode` | `VARCHAR(14) NULL`, FK `BRANCHS` | `NULL` = assina por todas as filiais |
| `Name` | `VARCHAR(100) NOT NULL` | |
| `TaxId` | `VARCHAR(14) NOT NULL` | CPF, só dígitos |
| `Email` | `VARCHAR(200) NOT NULL` | é a chave que o D4Sign usa para identificar o signatário no webhook |
| `Role` | `int NOT NULL` (`SignatoryRole`) | ver enum abaixo |
| `Order` | `int NOT NULL default 0` | ordem de exibição no bloco de assinaturas |
| `Active` | `bit NOT NULL default 1` | |
| — | índice único `(Email)` | o webhook correlaciona signatário por e-mail; dois cadastros com o mesmo e-mail seriam indistinguíveis |

**`BUSINESS_PARTNER_SIGNATORIES`** (`BusinessPartnerSignatory : BaseEntity`) — quem assina pelo
parceiro. Mesmas colunas de `COMPANY_SIGNATORIES` trocando `BranchCode` por
`CardCode VARCHAR(15) NOT NULL` **sem FK**: em modo SAPB1 a tabela `BUSINESS_PARTNERS` local
está vazia (mesma razão de `SalesContract.CardFName`). Índice em `CardCode` e índice único
`(CardCode, Email)` — a mesma pessoa pode assinar por dois parceiros, mas não duas vezes pelo
mesmo.

**`CONTRACT_DRAFTS`** (`ContractDraft : DocumentEntity`) — a minuta.

| Coluna | Tipo | Nota |
|---|---|---|
| `PurchaseContractKey` | `uniqueidentifier NULL`, FK `PURCHASE_CONTRACTS`, índice | |
| `SalesContractKey` | `uniqueidentifier NULL`, FK `SALES_CONTRACTS`, índice | |
| — | check `CK_CONTRACT_DRAFTS_ONE_CONTRACT` | exatamente uma das duas FKs preenchida |
| `ContractCode` | `VARCHAR(50) NOT NULL` | snapshot do código do contrato, para listar sem join |
| `Sequence` | `int NOT NULL` | 1, 2, 3… por contrato; identifica a minuta na tela ("Minuta 2 do PC-000123") |
| `TemplateKey` | `uniqueidentifier NOT NULL`, FK `CONTRACT_TEMPLATES` (`NoAction`) | |
| `DraftType` | `int NOT NULL` (`ContractDraftType`: `Contract=0`, `Amendment=1`, `Termination=2`) | contrato / aditivo / distrato |
| `Description` | `VARCHAR(200) NOT NULL` | |
| `BodyHtml` | `VARCHAR(MAX) NOT NULL` | HTML renderizado — **snapshot**, não se re-renderiza |
| `PlaceholdersJson` | `VARCHAR(MAX) NOT NULL` | dicionário resolvido no momento da criação; permite re-render e auditoria de "que valor entrou" |
| `Status` | `int NOT NULL` (`ContractDraftStatus`) | ver máquina de estados |
| `Provider` | `VARCHAR(20) NULL` | `"D4Sign"`, gravado no envio |
| `ExternalDocumentId` | `VARCHAR(100) NULL`, índice único filtrado (`IS NOT NULL`) | uuid do documento no D4Sign; único porque o webhook procura por ele |
| `SentAt`, `SignedAt`, `CanceledAt` | `datetime2 NULL` | `CanceledAt`/`CanceledBy` já vêm de `BaseEntity` |
| `LastError` | `VARCHAR(1000) NULL` | última falha de provedor, mostrada na tela |
| `SignedAttachmentKey` | `uniqueidentifier NULL` | chave do anexo do contrato onde o PDF assinado foi guardado |
| `BranchCode` | herdado de `DocumentEntity` | copiado do contrato |

Navegações `PurchaseContract.Drafts` e `SalesContract.Drafts`. `DocNumberKey` fica nulo: a
minuta não tem numeração própria, é "Minuta N do contrato X".

**`CONTRACT_DRAFT_SIGNERS`** (`ContractDraftSigner`, POCO como os anexos) — snapshot dos
signatários no momento do envio, um por pessoa.

| Coluna | Tipo | Nota |
|---|---|---|
| `Key` | `uniqueidentifier` PK | |
| `DraftKey` | FK `CONTRACT_DRAFTS` (`NoAction`), índice | |
| `Side` | `int` (`SignerSide`: `Company=0`, `Partner=1`) | |
| `Name`, `TaxId`, `Email` | como nos cadastros | `Email` é a chave de correlação com o webhook |
| `Role` | `int` (`SignatoryRole`) | |
| `Order` | `int` | ordem enviada ao D4Sign |
| `Status` | `int` (`SignerStatus`: `Pending=0`, `Signed=1`, `EmailFailed=2`) | |
| `SignedAt` | `datetime2 NULL` | |
| `LastMessage` | `VARCHAR(500) NULL` | mensagem do D4Sign (ex.: motivo da falha de e-mail) |

**`ContractChangeLogFields`** ganha `Draft = "Draft"` com descrição "Minuta". O log do contrato
registra "Minuta 2 enviada para assinatura", "Minuta 2 assinada", "Minuta 2 cancelada" — como
já faz com anexos.

**`FinishedContractMutationGuardInterceptor`**: `ContractDraft` e `ContractDraftSigner` ficam
**fora** da lista, com comentário no `switch`. Minuta é documentação, como anexo; um contrato
encerrado pode ter aditivo ou distrato.

#### Enum `SignatoryRole`

Espelha os 13 atos do D4Sign para não perder nenhum na migração da Tagui, mas os valores são
nossos; o mapeamento para o código `act` fica dentro do provider.

| Valor | Nome | D4Sign `act` | Rótulo pt-BR |
|---|---|---|---|
| 1 | `Sign` | 1 | Assinar |
| 2 | `Approve` | 2 | Aprovar |
| 3 | `Acknowledge` | 3 | Reconhecer |
| 4 | `SignAsParty` | 4 | Assinar como parte |
| 5 | `SignAsWitness` | 5 | Assinar como testemunha |
| 6 | `SignAsIntervening` | 6 | Assinar como interveniente |
| 7 | `AcknowledgeReceipt` | 7 | Acusar recebimento |
| 8 | `SignAsIssuerEndorserGuarantor` | 8 | Assinar como emissor, endossante e avalista |
| 9 | `SignAsIssuerEndorserGuarantorSurety` | 9 | … e fiador |
| 10 | `SignAsSurety` | 10 | Assinar como fiador |
| 11 | `SignAsPartyAndSurety` | 11 | Assinar como parte e fiador |
| 12 | `SignAsJointDebtor` | 12 | Assinar como responsável solidário |
| 13 | `SignAsPartyAndJointDebtor` | 13 | Assinar como parte e responsável solidário |

`TITULAR` e `TESTEMUNHA` da Tagui migram para `SignAsParty` e `SignAsWitness`.

### Modelos e placeholders

**Sintaxe `{{nome}}`**, nomes em **pt-BR** e minúsculas com underscore — é o usuário que os
digita no editor, portanto é texto de usuário. Os nomes são os da Tagui onde o campo tem o mesmo
significado, para que a migração dos modelos seja um replace de `$x`/`${x}` por `{{x}}`.

`ContractDraftPlaceholderResolver` (Application, `Services/ContractDrafts/`) produz um
`IReadOnlyDictionary<string,string>` com **todos os valores já formatados** (pt-BR: datas
`dd/MM/yyyy`, quantidades `#,##0.000`, preços `#,##0.00` ou até 8 casas quando houver, CNPJ/CPF
com máscara, extenso via `ContractDraftNumberSpeller`). Dados do parceiro passam por
`IBusinessPartnerService` (resolvido por modo SAPB1/STANDALONE) e nunca por navegação; dados da
empresa vêm de `Branch` do contrato e do setting `CompanyName`.

| Placeholder | Compra | Venda | Origem |
|---|---|---|---|
| `numero`, `complemento` | ✓ | ✓ | `Code`, `Complement` |
| `emissao`, `emissao_extenso` | ✓ | ✓ | `CreationDate` |
| `data_inicio_entrega`, `data_termino_entrega` | ✓ | ✓ | |
| `representante_nome` | ✓ | ✓ | `AgentName` |
| `corretor_nome`, `corretor_comissao` | ✓ | — | primeiro `PurchaseContractBroker`; vazio se não houver |
| `fornecedor_razao_social`, `fornecedor_nome_fantasia`, `fornecedor_cnpj` | ✓ | — | `IBusinessPartnerService` por `CardCode` |
| `fornecedor_endereco`, `fornecedor_bairro`, `fornecedor_cep`, `fornecedor_cidade`, `fornecedor_uf` | ✓ | — | endereço padrão do parceiro (`Address`: `Street`, `Block`, `ZipCode`, `City`, `State`) |
| `cliente_*` (mesmos 8 campos) | — | ✓ | idem |
| `local_entrega` | ✓ | ✓ | compra: `DeliveryLocationName`; venda: `DeliveryLocations` concatenados por "; " |
| `produto_descricao` | ✓ | ✓ | `ItemName` |
| `safra_descricao` | ✓ | ✓ | `HarvestSeason` |
| `quantidade`, `unidade_medida` | ✓ | ✓ | `TotalVolume`/`Volume`, `UnitOfMeasureCode` |
| `preco`, `moeda` | ✓ | ✓ | `StandardPrice`/`Price`, `StandardCurrency` |
| `valor_total`, `valor_total_extenso` | ✓ | ✓ | `TotalStandard` / `Volume × Price`, HALF_EVEN 2 casas |
| `tipo_frete` | ✓ | ✓ | `FreightTerms` por extenso |
| `condicao_pagamento`, `data_pagamento` | ✓ | ✓ | `PaymentTerms`, `StandardCashFlowDate` |
| `empresa_razao_social`, `empresa_cnpj`, `filial_nome` | ✓ | ✓ | `CompanyName`, `Branch.TaxId`, `Branch.BranchName` |
| `assinaturas_empresa`, `assinaturas_parceiro` | ✓ | ✓ | blocos HTML (tabela nome/CPF/papel) com os signatários **ativos** no momento da criação |

Os nomes da Tagui `quantidade_fiscal`/`quantidade_comercial`, `preco_fiscal`/`preco_comercial`,
`contribuicao_social` e `cessao_credito_*` não têm correspondente no SiagroB1 e não existem aqui.
A migração de modelos precisa tratá-los (remover ou mapear para `quantidade`/`preco`).

**Renderização** (`ContractDraftTemplateRenderer`, função pura `string Render(string html,
IReadOnlyDictionary<string,string> values)`): substitui `{{nome}}`, tolera espaços internos
(`{{ nome }}`), e **falha com `BusinessException` listando os placeholders desconhecidos** —
contrato com campo faltando não pode sair silenciosamente com o texto `{{x}}` no meio. A mesma
validação roda ao **salvar o modelo** (`ContractTemplatesValidate` chamado por Create/Update),
para o erro aparecer na edição e não na hora de gerar a minuta.

A função OData `ContractTemplatesListPlaceholders(ContractType)` devolve a lista (nome +
descrição pt-BR) para o editor mostrar.

### PDF

`IHtmlToPdfRenderer` (Domain/Interfaces) com `Task<byte[]> RenderAsync(string html,
CancellationToken ct)`. Implementação `ChromiumHtmlToPdfRenderer` em `SiagroB1.Infra/Pdf/`
(pacote `PuppeteerSharp`):

- Uma instância de browser por processo, criada preguiçosamente e protegida por `SemaphoreSlim`;
  cada render abre e fecha uma página (`SetContentAsync` + `PdfAsync`). A4, margens 20 mm,
  `PrintBackground = true`.
- Executável: `Signature:Pdf:ChromiumPath`. Se vazio, `BrowserFetcher` baixa para
  `<ContentRoot>/chromium` no primeiro uso. `Signature:Pdf:NoSandbox` (default `false`) adiciona
  `--no-sandbox`, necessário em alguns Windows Service/Linux — só liga quando o log pedir.
- `Program.cs` avisa no boot (`WarnIfContractDraftPdfIsUnavailable`) se `Signature:Enabled` e o
  Chromium não está acessível. Não derruba o serviço: o resto do sistema não depende disso.
- O HTML que vai para o Chromium é o `BodyHtml` embrulhado num documento com `<meta charset>`
  e uma folha de estilo mínima (fonte, tamanho, `table { border-collapse }`) definida em
  `ContractDraftPdfLayout` — a mesma que o editor UI5 aplica no preview, para o que se vê ser o
  que se imprime.

Testes usam `FakeHtmlToPdfRenderer` (devolve bytes fixos). O renderer real tem um teste de fumaça
marcado `[Trait("Category","Chromium")]` que é pulado quando o executável não existe.

### Ciclo de vida da minuta

Serviços em `SiagroB1.Application/Services/ContractDrafts/`, um por operação, registrados à mão
em `AddApplicationServices()`.

```
              Create
                │
                ▼
              Draft ──Update/Delete──▶ (só aqui)
                │ SendToSignature (síncrono: PDF → upload → signatários → webhook → send)
                │   falha de provedor: volta a Draft, LastError preenchido, erro na tela
                ▼
        AwaitingSignature ──webhook type_post=4──▶ PartiallySigned
                │                                        │
                │ webhook type_post=1 / reconciliação    │
                ▼                                        ▼
              Signed ◀───────────────────────────────────┘
                (PDF assinado vira anexo do contrato; contrato.SignatureStatus = Signed)

        AwaitingSignature | PartiallySigned ──Cancel──▶ Canceled (cancela no D4Sign)
        AwaitingSignature | PartiallySigned ──webhook type_post=3──▶ Canceled (cancelado lá)
```

| Serviço | Regra |
|---|---|
| `ContractDraftsCreateService` | contrato existe e `Status != Canceled`; modelo ativo e com `ContractType` compatível; resolve placeholders, renderiza, grava `PlaceholdersJson` + `BodyHtml`, `Sequence = max+1` do contrato, `BranchCode` do contrato, `Status = Draft`. Registra no change log do contrato ("Minuta N criada"). |
| `ContractDraftsUpdateService` | só `Draft`; altera `Description`, `DraftType` e `BodyHtml` (edição manual do texto renderizado, como na Tagui). Não re-resolve placeholders. |
| `ContractDraftsDeleteService` | só `Draft`. |
| `ContractDraftsGetPdfService` | qualquer status; renderiza `BodyHtml` na hora. Se `Signed` e `SignedAttachmentKey` preenchido, devolve o PDF **assinado** do anexo em vez do render. |
| `ContractDraftsSendToSignatureService` | só `Draft`; `Signature:Enabled`; signatários ativos da empresa (`BranchCode == contrato.BranchCode || BranchCode == null`) e do parceiro (`CardCode`), ambos não vazios senão `BusinessException` dizendo qual lado falta; grava snapshot em `CONTRACT_DRAFT_SIGNERS` (empresa primeiro, depois parceiro, por `Order`); chama `IESignatureProvider.SendAsync`; sucesso → `Provider`, `ExternalDocumentId`, `SentAt`, `Status = AwaitingSignature`, contrato `SignatureStatus = AwaitingSignature` via `*SetSignatureStatusService` (que já escreve o change log), mais linha "Minuta N enviada para assinatura"; falha → `LastError`, status continua `Draft`, signers snapshot descartado, `BusinessException` com a mensagem. |
| `ContractDraftsCancelService` | `AwaitingSignature` ou `PartiallySigned`; `IESignatureProvider.CancelAsync`; `Status = Canceled`, `CanceledAt/By`; **não toca** `SignatureStatus` do contrato (o usuário pode ter assinado em papel). Change log "Minuta N cancelada". |
| `ContractDraftsApplyProviderStateService` | porta única de escrita a partir do provedor (webhook e reconciliação usam a mesma): recebe `ESignatureDocumentState` (status do documento + lista de signatários com e-mail/assinado/data), atualiza `CONTRACT_DRAFT_SIGNERS` por e-mail, e move o status: todos assinados ou documento finalizado → `Signed` + download do PDF assinado → `*AttachmentsCreateService` com `Description = "Minuta N assinada"` → `SignedAttachmentKey` → contrato `SignatureStatus = Signed`; algum assinado → `PartiallySigned`; cancelado lá → `Canceled`. Idempotente: estado igual não grava nada. |
| `ContractDraftsListByContractService` | DTO sem `BodyHtml`/`PlaceholdersJson`, com signers. |

A ordem das transações segue a casa: change log e set-status são "enqueue-only", quem salva é o
serviço da operação, dentro de uma transação. A chamada HTTP ao D4Sign acontece **antes** de
`SaveChanges` e o resultado decide o que se grava; não há estado intermediário "enviando".

### Provedor de assinatura

`IESignatureProvider` (Domain/Interfaces):

```csharp
Task<ESignatureSendResult>   SendAsync(ESignatureSendRequest request, CancellationToken ct);
Task<ESignatureResult>       CancelAsync(string externalDocumentId, CancellationToken ct);
Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct);
Task<byte[]?>                DownloadSignedAsync(string externalDocumentId, CancellationToken ct);
```

`ESignatureSendRequest` = título do documento, bytes do PDF, lista de signatários (nome, e-mail,
`SignatoryRole`, ordem), URL do webhook. Resultados nunca lançam exceção por falha do provedor:
devolvem `Succeeded`, `ErrorMessage` e `Transient` (429/408/5xx/timeout), no molde de
`WhatsAppSendResult`.

`D4SignProvider` (`SiagroB1.Infra/ESignature/D4Sign/`), `HttpClient` tipado registrado em
`Program.cs` ao lado do WhatsApp, `BaseAddress = Signature:D4Sign:BaseUrl`, timeout 30 s. Lê
`TokenApi`, `CryptKey`, `SafeId` inline de `IConfiguration` a cada chamada (sem `IOptions`, como
o resto do solution). Chamadas, na ordem do `SendAsync`:

| Passo | D4Sign | Nota |
|---|---|---|
| upload | `POST /documents/{safeId}/upload` (multipart `file`, `uuid_folder` opcional) | credenciais em header nesta chamada, query nas demais — igual à Tagui |
| signatários | `POST /documents/{uuid}/createlist` `{ signers: [{ email, act, foreign:"0", certificadoicpbr:"0", assinatura_presencial:"0", docauth:"0", docauthandselfie:"0", embed_methodauth:"email", upload_allow:"0" }] }` | `act` do mapa `SignatoryRole` |
| webhook | `POST /documents/{uuid}/webhooks` `{ url }` | url = `Signature:D4Sign:WebhookBaseUrl` + `/hooks/d4sign/` + `WebhookSecret` |
| enviar | `POST /documents/{uuid}/sendtosigner` `{ message, skip_email:"0", workflow:"0", tokenAPI }` | `workflow "0"` = sem ordem obrigatória, como hoje |
| cancelar | `POST /documents/{uuid}/cancel` | |
| estado | `GET /documents/{uuid}` + `GET /documents/{uuid}/list` | `statusId` do documento (`4` finalizado, `6` cancelado) e `signed`/`date`/`email` por signatário |
| baixar | `POST /documents/{uuid}/download` `{ type:"PDF", language:"pt" }` → `{ url }` → `GET url` | PDF com as assinaturas e o certificado do D4Sign |

Se o upload dá certo e um passo seguinte falha, o `SendAsync` tenta `cancel` do uuid criado e
devolve falha: o documento órfão no cofre do D4Sign é o único efeito colateral, e fica logado
com o uuid.

Segredos nunca entram em `LastError` nem em log: mensagens de erro do D4Sign são truncadas em
300 caracteres, e o `HttpRequestException.Message` é descartado como no `PlugZapiWhatsAppSender`.

### Webhook

`SiagroB1.Web/Hooks/D4SignWebhookEndpoint.cs`, `MapD4SignWebhook()` no `Program.cs` como o
`MapTruckScaleWebSocket()`:

- Rota `POST /hooks/d4sign/{secret}`. Compara `secret` com `Signature:D4Sign:WebhookSecret` por
  `CryptographicOperations.FixedTimeEquals` **antes de qualquer outra coisa**. Segredo não
  configurado ⇒ **401 sempre** (fail-closed, ao contrário de `ScaleClientAuth`) e aviso no boot.
- Lê o `form-data` (`uuid`, `type_post`, `email`, `message`). Só `uuid` é usado para achar a
  minuta; **o estado vem de `GetStateAsync`**, não do payload — o D4Sign não assina o webhook e
  qualquer um que descubra a URL poderia postar `type_post=1`.
- `uuid` desconhecido ⇒ 200 com log em `Warning` (pode ser documento criado à mão no cofre; não
  vale gastar as 7 tentativas do D4Sign). Minuta encontrada ⇒ `ContractDraftsApplyProviderStateService`
  ⇒ 200. Erro interno ⇒ 500 (o D4Sign reenvia: imediato, 3× em 1 h, 2× em 6 h, 1× em 12 h).
- Gateway: rota nova `hooks-route`, `Path: /hooks/{**catch-all}`, `AuthorizationPolicy: null`,
  cluster `backend`, nos **três** `appsettings*.json` do Gateway. Nada mais no Web fica exposto
  por ela: só esse endpoint responde sob `/hooks`.

### Reconciliação

`ContractDraftsReconcileJob` (`Application/Jobs/`), recurring `contract-drafts-reconcile`,
`*/30 * * * *`, `[AutomaticRetry(Attempts = 0)]`, registrado em `Program.cs` só quando
`Signature:Enabled` (e `RecurringJob.RemoveIfExists` quando não — sem job órfão). Seleciona
minutas em `AwaitingSignature`/`PartiallySigned` com `UpdatedAt < now − 6 h`, no máximo 50, e
para cada uma chama `GetStateAsync` + `ApplyProviderState`. Argumento do job enfileirado é só a
`Key` (o painel do Hangfire mostra argumentos).

### Configuração

Seção nova em `SiagroB1.Web/appsettings.json` e nas quatro variantes, no molde de
`Notifications:WhatsApp`:

```json
"Signature": {
  "Enabled": false,
  "Provider": "D4Sign",
  "D4Sign": {
    "BaseUrl": "https://sandbox.d4sign.com.br/api/v1",
    "TokenApi": "", "CryptKey": "", "SafeId": "",
    "WebhookBaseUrl": "", "WebhookSecret": ""
  },
  "Pdf": { "ChromiumPath": "", "NoSandbox": false }
}
```

Produção troca `BaseUrl` por `https://secure.d4sign.com.br/api/v1`. `WebhookBaseUrl` é a URL
pública do Gateway (o D4Sign precisa alcançá-la). Os arquivos já são `CopyToPublishDirectory=Never`
e os deploys fornecem os seus; o base fica com segredos em branco.

`Signature:Enabled = false` ⇒ a tela de minutas funciona (criar, editar, PDF) e só "Enviar para
assinatura" responde com `BusinessException` "Assinatura eletrônica não está habilitada neste
ambiente."

### API (OData)

Entity sets: `ContractTemplates`, `CompanySignatories`, `BusinessPartnerSignatories`,
`ContractDrafts`, `ContractDraftSigners` — os três primeiros com `ODataBaseController<T,Guid>`
(CRUD), e `ContractTemplatesController` sobrescrevendo Post/Patch para validar placeholders.
`ContractDrafts` e `ContractDraftSigners` só leitura pelo set; mutações por action.

| Tipo | Nome | Parâmetros |
|---|---|---|
| Action | `ContractDraftsCreate` | `ContractType` (string `Purchase`/`Sales`), `ContractKey`, `TemplateKey`, `DraftType` (string), `Description` |
| Action | `ContractDraftsUpdate` | `Key`, `Description`, `DraftType`, `BodyHtml` |
| Action | `ContractDraftsDelete` | `Key` |
| Action | `ContractDraftsSendToSignature` | `Key` |
| Action | `ContractDraftsCancel` | `Key` |
| Function | `ContractDraftsListByContract(ContractType, ContractKey)` | DTO com signers, sem HTML |
| Function | `ContractDraftsGetBody(Key)` | `BodyHtml` para o editor/preview |
| Function | `ContractDraftsDownloadPdf(Key)` | `File(bytes, "application/pdf", "{ContractCode}-minuta-{Sequence}.pdf")` |
| Function | `ContractTemplatesListPlaceholders(ContractType)` | nome + descrição |
| Function | `ContractDraftsRefreshState(Key)` | força `GetStateAsync` + apply; botão "Atualizar situação" na tela |

Enums e datas como string nos parâmetros, `.Optional()` onde couber, `parameters is null` tratado
como nos controllers de anexo. Escada de exceções: `NotFoundException` → 404,
`BusinessException`/`DefaultException`/`ApplicationException` → 400, resto → 500.

### Tela (siagro-b1-frontend, plano à parte)

- **Modelos de Contrato** (`contractTemplates`): lista + edição com
  `sap.ui.richtexteditor.RichTextEditor` (TinyMCE), painel lateral com os placeholders da função
  `ContractTemplatesListPlaceholders` (clique insere `{{nome}}`), preview com o mesmo CSS de
  `ContractDraftPdfLayout`.
- **Signatários da Empresa** (`companySignatories`): CRUD simples, filial opcional.
- **Signatários do parceiro**: aba nova em `parceirosNegocio` (funciona nos dois modos porque a
  tabela é local, chaveada por `CardCode`).
- **Aba "Minutas"** em `purchaseContracts` e `salesContracts`: tabela (sequência, tipo,
  descrição, situação, enviado em, assinado em), botões Nova minuta (diálogo: modelo filtrado por
  tipo, tipo de minuta, descrição), Editar texto (só Draft), PDF, Enviar para assinatura, Cancelar,
  Atualizar situação; expandir linha mostra os signatários com status individual.
- Menus: migration `CommonContext` `AddContractDraftMenus` com `contractTemplates` e
  `companySignatories` no grupo "Cadastros" (ordem a inspecionar), `ADMIN` em `ROLE_MENUS`.

### Testes

`SiagroB1.Application.Tests/ContractDrafts/` com `ContractDraftsTestContext` parcial como o de
`WarehouseReconciliations`, `FakeHtmlToPdfRenderer`, `FakeESignatureProvider` (scriptável:
`SucceedsWith(uuid)`, `FailsTransiently()`, `StateIs(...)`).

- `ContractDraftTemplateRendererTests`: substituição, espaços, desconhecidos listados, HTML sem
  placeholder passa intacto.
- `ContractDraftPlaceholderResolverTests`: compra e venda; modo SAPB1 (parceiro só via
  `FakeBusinessPartnerService`) e STANDALONE; formatação; `assinaturas_*` só com ativos e por
  filial.
- `ContractDraftsCreateServiceTests`, `UpdateServiceTests`, `DeleteServiceTests`: guardas de
  status, `Sequence`, modelo inativo/incompatível, contrato cancelado.
- `ContractDraftsSendToSignatureServiceTests`: sem signatário de cada lado; `Enabled=false`;
  sucesso grava tudo e move `SignatureStatus` do contrato com change log; falha deixa `Draft` com
  `LastError` e sem signers; reenvio de `AwaitingSignature` é recusado (o defeito da Tagui).
- `ContractDraftsApplyProviderStateServiceTests`: parcial, finalizado (anexo criado,
  `SignedAttachmentKey`, contrato `Signed`), cancelado lá, idempotência (aplicar duas vezes não
  duplica anexo nem log).
- `D4SignProviderTests` com `StubHttpMessageHandler`: sequência de chamadas e corpos, `act` por
  papel, credenciais em header no upload e query nas demais, falha no meio ⇒ cancel do uuid,
  classificação `Transient`, segredo nunca aparece em `ErrorMessage`.
- `D4SignWebhookEndpointTests` (WebApplicationFactory mínima ou teste do handler puro): segredo
  errado/ausente ⇒ 401; uuid desconhecido ⇒ 200; estado vem do provider, não do form.
- `ContractDraftEdmModelTests`: sets, actions e functions com seus parâmetros e tipos string.
- `AppDbContextModelTests`: check constraint, índice único filtrado de `ExternalDocumentId`,
  `DeleteBehavior.NoAction`.
- Manual antes do merge: fluxo completo no **sandbox do D4Sign** com dois signatários reais,
  webhook chegando pelo Gateway (túnel ou ambiente de homologação), PDF assinado anexado.

## Riscos e decisões conscientes

- **Chromium em produção**: primeiro deploy precisa do binário (download pelo `BrowserFetcher`
  exige saída para a internet) e possivelmente `NoSandbox`. Validar em homologação da Yokotobi
  antes de prometer data.
- **Correlação de signatário por e-mail**: se duas pessoas do mesmo lado tiverem o mesmo e-mail,
  o webhook marca as duas. Índice único `(Email)` nos dois cadastros de signatário evita isso.
- **Snapshot vs. dado vivo**: a minuta é fotografia do contrato no momento da criação. Se o
  contrato mudar depois, a minuta não muda — o usuário cria outra. É o comportamento da Tagui e
  o único auditável.
- **Sem ordem obrigatória de assinatura** (`workflow "0"`): igual à Tagui. Ordem sequencial é
  um flag a mais no futuro, não uma mudança de modelo.
- **`SignatureStatus` manual continua livre**: a action de set-status não ganha guarda. A minuta
  escreve nele nos eventos de envio e assinatura; o usuário pode sobrescrever a qualquer momento.
