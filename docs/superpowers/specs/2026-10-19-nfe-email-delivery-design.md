# Envio automático de NF-e por e-mail (DANFE + XML) — design

Data: 2026-10-19 · Modo: **somente STANDALONE** · Repos: backend + frontend (`feature/nfe-email`)

## Por quê

Hoje, a cada NF-e autorizada ou cancelada, alguém baixa o XML e o DANFE e envia manualmente ao
parceiro. O objetivo é eliminar esse trabalho: o sistema envia sozinho, para todos os e-mails
cadastrados no PN (ex.: `financeiro@cliente.com.br` e `compras@cliente.com.br`), e permite
reenviar pela própria nota quando o cliente diz que não recebeu.

## Objetivos

1. Toda NF-e **própria** que passar a **Autorizada** ou **Cancelada** gera um e-mail automático
   ao PN da nota.
2. O PN passa a ter **uma lista** de e-mails para NF-e (substitui o campo único `NfeEmail`).
3. Cada **empresa** (banco) tem seu próprio remetente/SMTP, configurado por tela. Hoje só a
   CEAGUI será configurada (Hostinger, 587, STARTTLS, `nfe@ceagui.com.br`).
4. A nota mostra o histórico de envios e tem botão **"Enviar e-mail"** para reenviar,
   permitindo ajustar os destinatários.

## Não objetivos

- CC-e, inutilização, notas de terceiros (`IssuerType = ThirdParty`), consulta de NF-e de fornecedor.
- Modo SAPB1 (nenhuma tela/menu/job novo aparece ou roda lá).
- Cópia automática para e-mail interno da empresa; templates de e-mail editáveis.

## Anti-objetivos (falha mesmo que "funcione")

- Uma falha de SMTP **nunca** pode impedir, atrasar ou desfazer a autorização/cancelamento da nota.
- O mesmo evento não pode gerar e-mails duplicados (a consulta de situação reaplica o resultado).
- Ambiente de desenvolvimento/homologação não pode mandar e-mail real para parceiro por acidente.
- A senha do SMTP nunca aparece em claro: nem no banco, nem em log, nem na resposta da API,
  nem em arquivo versionado.

## Escopo das notas

Os dois documentos que emitem implementam `INfeDocument`: `SalesInvoice` (saída, devolução de
compra) e `PurchaseInvoice` com `IssuerType = Own` (entrada própria, devolução de venda,
contra-nota do produtor, complementos de valor/ICMS). Todos entram. O destinatário é sempre o PN
de `INfeDocument.CardCode`.

## Desenho

### 1. E-mails do PN

- Nova tabela `BUSINESS_PARTNER_NFE_EMAILS` (`BusinessPartnerNfeEmail`: Key, CardCode, Email,
  LineNum), filha de `BUSINESS_PARTNERS`, índice único (CardCode, Email).
- Migration copia o `NfeEmail` atual (quando preenchido) como primeira linha e **remove** a coluna
  `NfeEmail`. O `dest/email` do XML (`NfeIssueInputAssembler.cs:194`) passa a usar o primeiro
  e-mail da lista (o leiaute aceita um só).
- Validação: formato de e-mail, sem duplicados no mesmo PN, máximo 10.
- ⚠️ Tabela filha nova: o delete do PN precisa apagar as linhas (ver memória
  child-table-breaks-parent-delete-service) — cascade na FK + teste.
- Frontend: no formulário do PN (só STANDALONE), o `Input` de "E-mail NF-e" vira uma pequena
  tabela editável (adicionar/remover linha).

### 2. Configuração de e-mail por empresa

- Nova tabela `EMAIL_SETTINGS` no banco **da empresa** (`AppDbContext`), linha única: Host, Port,
  UseStartTls, UserName, PasswordEncrypted, FromAddress, FromName, AutoSendEnabled (bool),
  UpdatedAt/UpdatedBy.
- Senha criptografada com o mesmo mecanismo da senha do certificado A1 (`Nfe:CertificateKey`).
  A API nunca devolve a senha; o PATCH sem senha mantém a atual.
- Nova tela **"Configuração de e-mail"** (menu de configurações, STANDALONE, papel ADMIN) com
  botão **"Enviar e-mail de teste"** (para um endereço informado, retorna o erro do SMTP na tela).
  ⚠️ precisa de migration de menu e regra de permissão (memória new-screen-needs-menu-migration).
- Sem linha configurada, ou `AutoSendEnabled = false`: nenhum envio automático, sem erro.
- **Trava global de ambiente:** a chave existente `Email:Enabled` (appsettings, padrão `false`)
  continua valendo também no Web: com `false`, o envio só vai para o log (como o
  `SmtpEmailSender` já faz). Produção CEAGUI precisa de `Email:Enabled=true`.

### 3. Remetente com anexos

- `IEmailSender` ganha uma sobrecarga que recebe uma mensagem completa: lista de destinatários,
  assunto, HTML, anexos (nome, bytes, content-type) e as configurações SMTP a usar. Retorna
  sucesso/erro com a mensagem do SMTP (para log e para o teste).
- `SmtpEmailSender` (MailKit) passa a ser registrado também no `SiagroB1.Web`. A recuperação de
  senha do Gateway não muda.

### 4. Fila de envio (outbox), no padrão da notificação de WhatsApp

- Nova tabela `NFE_EMAIL_MESSAGES` no banco da empresa: Key, DocumentType (Sales/Purchase),
  DocumentKey (sem FK, como no WhatsApp), EventType (Authorized/Cancelled), Trigger
  (Automatic/Manual), Recipients (snapshot, `;`), Status (Pending/Sent/Failed/Skipped),
  AttemptCount, LastAttemptAt, LastError, SentAt, CreatedAt, CreatedBy.
- **Gatilhos** (os dois únicos pontos de gravação, cobrem emissão e "Consultar situação"):
  - autorização: `NfeResultHandlerBase.ApplyAsync`, no mesmo `SaveChanges` que grava Authorized;
  - cancelamento: `NfeCancellationHandlerBase.ApplyRegisteredAsync`, idem.
  - Só enfileira em STANDALONE, com `AutoSendEnabled` ligado.
- **Idempotência:** índice único filtrado em (DocumentType, DocumentKey, EventType) onde
  `Trigger = Automatic` — reaplicar a autorização não cria segunda linha (verificar antes de
  inserir; o índice é a rede). ⚠️ índice filtrado exige QUOTED_IDENTIFIER.
- **Sweep** por empresa a cada minuto (`CompanyScopedJobRunner`, como `contract-notification-sweep`)
  pega Pending com 1 min de carência e enfileira o dispatch com `companyCode` como 1º parâmetro.
- **Dispatch** (fila Hangfire própria `email`, 1 worker): sai se não estiver Pending; resolve
  destinatários (snapshot da linha; vazio → Skipped "PN sem e-mail de NF-e"); monta anexos;
  envia; grava Sent ou erro. Até 3 tentativas (30 s, 2 min, 10 min); depois Failed.
- **Reenvio manual:** cria nova linha `Trigger = Manual` com os destinatários escolhidos na tela
  e enfileira direto.

### 5. Conteúdo

- **Autorizada** — assunto `NF-e nº {nNF} série {serie} – {xNome emitente} – Autorizada`;
  anexos `{chave}-procNFe.xml` + `DANFE-{nNF}.pdf`.
- **Cancelada** — assunto `... – Cancelada`; anexos `{chave}-procEventoNFe.xml` + DANFE com o
  carimbo "CANCELADO". Se o cancelamento foi feito fora do Siagro (sem XML do evento), vai só o
  DANFE e o corpo diz isso.
- Corpo HTML curto em pt-BR: emitente (nome/CNPJ), destinatário, número/série, chave de acesso,
  data de emissão, valor total, protocolo; "Este é um e-mail automático".
- Dados lidos do procNFe/evento armazenado (`LatestAuthorizedXmlAsync`, `LatestXmlAsync`).

### 6. DANFE

O DANFE já é gerado pelo `SiagroB1.Reports` (`DanfeReportService.GeneratePdfAsync` /
`GeneratePurchasePdfAsync`, com logo, fontes e .frx de lá). O Web **não** referencia o Reports.
Decisão: o dispatch obtém o PDF **chamando o Reports por HTTP interno** (nova chave
`Reports:InternalBaseUrl`, ex. `http://localhost:8081`), informando a empresa — um único
renderizador evita duplicar fontes/logo/template (que já deram trabalho no Linux). O builder
confirma como o Reports resolve a empresa numa chamada sem cookie (BasicAuth + `X-Company`
existe em `BasicAuthenticationHandler`) e escolhe a credencial de serviço; se isso se mostrar
inviável, a alternativa é extrair a renderização para uma biblioteca compartilhada — decisão a
registrar no plano.

### 7. Telas da nota

Nos Detail de Documento de Saída e de Entrada (só notas próprias, STANDALONE):
- Botão **"Enviar e-mail"** habilitado quando a NF-e está Autorizada ou Cancelada → diálogo com
  os e-mails do PN pré-preenchidos (editáveis) → cria o envio manual (evento conforme o status
  atual da nota).
- Seção/aba **"E-mails enviados"**: data, evento, automático/manual, destinatários, situação,
  erro, quem disparou.

## Restrições

- .NET 10 / OData v4 / EF Core SQL Server / Hangfire / OpenUI5 TS, convenções dos CLAUDE.md
  (inglês no código, pt-BR para o usuário; um serviço por operação, registrado à mão).
- Migrations: `AppDbContext` (tabelas da empresa + menu); aplicadas em **todas** as empresas no
  deploy (Web recusa subir com migration pendente).
- Testes em `SiagroB1.Application.Tests` (TDD): gatilho único por evento, não duplica na
  reconsulta, não enfileira em SAPB1/desligado, dispatch Skipped sem destinatário, falha
  registrada, migração do `NfeEmail`, delete do PN.

## Decisões tomadas por mim (Claude), não pelo usuário

Lista separada para revisão: conteúdo/assunto do e-mail; DANFE "CANCELADO" no cancelamento;
um e-mail com todos no "Para"; máximo 10 e-mails; remoção da coluna `NfeEmail`; fila própria
em vez de reaproveitar a do WhatsApp; DANFE via HTTP ao Reports; papel ADMIN na configuração;
trava global `Email:Enabled`; 3 tentativas.

## Esclarecimentos (revisão do builder)

- **Chave da senha:** uma única `Nfe:CertificateKey` por implantação (como o certificado A1),
  embora a tabela fique no banco de cada empresa. Sem chave nova.
- **DANFE sem cookie:** nova rota interna no Reports (ex. `POST /internal/danfe/{tipo}/{key}`)
  protegida por um segredo compartilhado (`Reports:InternalApiKey`, mesmo valor no Web e no
  Reports; vazio = rota desligada) e com a empresa passada em cabeçalho, que a rota usa para
  `ICompanyContext.Set` antes de renderizar. A rota não é exposta pelo Gateway (o YARP só repassa
  o que estiver nas rotas configuradas; conferir que `/reports/**` não a alcança — usar prefixo
  fora de `/reports`). Web e Reports rodam no mesmo host nas VPS. Sem usuário de serviço.
- **Ambientes:** `Email:Enabled` fica `false` por padrão em todo appsettings versionado; só a
  produção CEAGUI recebe `true` no deploy. As tabelas são criadas em todas as empresas
  (migrations são únicas); a migration de menu segue o padrão das telas só-STANDALONE já
  existentes, e os gatilhos/sweep não fazem nada fora de STANDALONE.
- **Notas antigas:** o envio automático só nasce numa **transição** de status — o handler compara
  o `NfeStatus` anterior ao gravado: se já era Authorized (ou Cancelled), não enfileira. Assim
  "Consultar situação" numa nota antiga nunca dispara e-mail. Sem data de corte.
- **Cancelada fora do Siagro** (descoberta pela consulta): também envia automaticamente, só com o
  DANFE "CANCELADO" e o aviso no corpo.
- **Carimbo CANCELADO:** já existe no `DanfeReportService` (aplica quando a nota está
  Cancelled); nenhuma mudança no `.frx` vendorizado.
- **Remoção do `NfeEmail`:** só é usado no formulário do PN (STANDALONE), no
  `BusinessPartnerService` e no `dest/email` do XML (que só existe em STANDALONE); scripts
  avulsos (`dados-tagui/*.mjs`) ficam fora. Em SAPB1 a tabela nova não é usada. Coluna removida
  na mesma migration que copia os dados.

## Fica a critério do builder

Nomes exatos de classes/endpoints, layout das telas, formato do corpo HTML, como compartilhar
o código entre Sales/Purchase (base genérica como nos handlers existentes).

## Notas de deploy

- Migrations AppContext (todas as empresas): `AddBusinessPartnerNfeEmails`, `AddEmailSettings`,
  `AddNfeEmailMessages`, `AddNfeEmailMessageEnqueuedAt`. CommonContext: `AddEmailSettingsMenu`.
- Web: `Email:Enabled=true` (só produção CEAGUI), `Reports:InternalBaseUrl` (ex. `http://localhost:8081`),
  `Reports:InternalApiKey`; `Nfe:CertificateKey` já existente. Reports: `Reports:InternalApiKey` com o mesmo valor.
- Depois do deploy: preencher "Configuração de e-mail" da empresa, "Enviar e-mail de teste" para um
  endereço interno e só então ligar "Envio automático". Conferir os e-mails de NF-e dos PNs.
