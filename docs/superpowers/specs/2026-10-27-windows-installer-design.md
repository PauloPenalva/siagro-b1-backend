# Instalador Windows do SiagroB1 (assistente visual) — desenho

Data: 2026-10-27 · Branch: `feature/windows-installer`

## Intenção

Hoje o deploy Windows (Yokotobi) é manual: copiar binários, editar `appsettings` à mão nos três
serviços, aplicar migrations com `dotnet ef`, e rodar `Install-SiagroB1-{Web,Gateway,Reports}-Service.ps1`
(que criam o serviço via `sc.exe` com `--environment` e `--urls http://*:<porta>`). O medo principal
está nas atualizações: sobrescrever configuração do cliente, esquecer chave nova, esquecer migration
(o Web recusa subir — `PendingMigrationsGuard`).

Queremos um **assistente próprio em .NET** (Avançar → Avançar → Concluir) que faça instalação nova e
atualização, com foco em editar e preservar os `appsettings` de forma segura.

## Decisões do usuário

- Assistente próprio em .NET (não Inno Setup/WiX).
- Frontend é servido pelo **Gateway** (`UseDefaultFiles` + `UseStaticFiles` no `wwwroot` do Gateway).
- Levantamento dos campos por cliente: feito (seção "Configuração").

## Objetivos

1. Um único `SiagroB1-Setup-<versão>.exe` que contém Web, Gateway, Reports e o frontend buildado.
2. Instalação nova: coleta configuração, copia arquivos, aplica migrations, cria e inicia os serviços.
3. Atualização: detecta instalação existente, pré-preenche telas com os valores atuais, faz backup,
   troca binários, **mescla** appsettings, aplica migrations, reinicia serviços.
4. Perguntar uma única vez o que é comum aos 3 serviços (SQL, bancos, modo ERP, empresa, portas) e
   gravar os três arquivos coerentes (chave interna Web↔Reports igual, endereços do YARP certos).
5. Rollback: se algo falhar após parar os serviços, restaurar o backup e religar a versão anterior.

## Não-objetivos

- `SiagroB1.Client` (leitor de balança) — fica fora nesta versão (deploy isolado, hardware próprio).
- Linux/VPS (CEAGUI, MH Agro rodam em Linux) — fora.
- IIS, certificados HTTPS, firewall do Windows (só oferecer, opcional, regra de firewall da porta do Gateway — ver "calls do builder").
- Atualização automática pela internet.
- Instalar SQL Server ou o runtime .NET (publicação é self-contained).

## Anti-objetivos (falha mesmo se "funcionar")

- Sobrescrever um valor que o cliente configurou.
- Deixar o sistema parado sem caminho de volta.
- Gravar senha em log ou no relatório final.
- Aplicar migration pelo boot da aplicação (`Migrate()` no `Program.cs`) — descartado de propósito,
  ver comentário em `PendingMigrationsGuard`.

## Arquitetura

Novo projeto `installer/SiagroB1.Installer` (WPF, `net10.0-windows`), **fora** do `SiagroB1.sln`
principal (solução própria `installer/SiagroB1.Installer.sln`) para não pesar o build do dia a dia,
mais `installer/SiagroB1.Installer.Tests` (xUnit) para a lógica pura.

Camadas dentro do projeto:

- `Core/` (sem WPF, testável): `InstallationDetector`, `AppSettingsMerger`, `SettingsModel` ↔ JSON
  dos 3 serviços, `ServiceManager` (wrapper sobre `sc.exe`/`ServiceController`), `BackupManager`,
  `MigrationRunner`, `InstallPlan` (lista ordenada de passos com `Execute`/`Rollback`).
- `Wizard/` (WPF, MVVM simples sem framework externo — decisão do builder): uma `Window` com área
  de página + Voltar/Avançar/Cancelar; cada página é um `UserControl` + ViewModel.

Exige elevação: `app.manifest` com `requireAdministrator`.

### Empacotamento

Script `installer/build-installer.ps1`:

1. `dotnet publish` de Web, Gateway, Reports — `-c Release -r win-x64 --self-contained`.
2. `yarn build` em `../siagro-b1-frontend` (único build válido — o cachebuster depende dele) e cópia
   de `dist/` para o `wwwroot` do Gateway publicado.
3. `dotnet ef migrations bundle` para `AppDbContext` e `CommonDbContext` (startup: Web,
   `--self-contained -r win-x64`) → `efbundle-app.exe`, `efbundle-common.exe`.
4. Compacta tudo em `payload.zip` + `manifest.json` (versão do frontend e commit do backend).
5. Publica o instalador como single-file self-contained com o `payload.zip` como recurso embutido
   → `SiagroB1-Setup-<versão>.exe`. (Se o tamanho tornar o recurso embutido inviável, alternativa
   aceita: `.exe` + `payload.zip` lado a lado — call do builder.)

O pacote leva só os `appsettings.json` **padrão** de cada serviço; nunca os de cliente.

### Layout instalado

```
C:\IdxSistemas\SiagroB1\<Instância>\        (padrão Instância = Producao)
  Web\  Gateway\  Reports\                   binários + appsettings.json (padrão da versão)
  install.json                               versão, serviços, portas, ambiente, data
  backups\<aaaaMMdd-HHmmss>\                 cópia das 3 pastas antes de cada atualização
  logs\setup-<aaaaMMdd-HHmmss>.log
```

A configuração do cliente vai em `appsettings.<Ambiente>.json` em cada pasta (ambiente padrão
`Production`, passado por `--environment` como hoje). O `appsettings.json` padrão é trocado a cada
versão; o arquivo do ambiente é do cliente e só é **mesclado**.

Instância permite mais de uma instalação na mesma máquina (ex.: Producao e Homologacao), com nomes de
serviço `siagrob1-<instância>-web|gateway|reports` e portas próprias.

### Detecção de instalação existente

Por ordem: `install.json` na pasta escolhida; senão, serviços cujo `binPath` aponte para
`SiagroB1.Web.exe`/`Gateway`/`Reports` (cobre a instalação Yokotobi feita pelos scripts, nomes
`siagrob1webservice` etc.). Na migração de uma instalação antiga o assistente lê ambiente e porta do
`binPath`, adota os nomes de serviço existentes e grava `install.json`.

## Páginas do assistente

1. **Boas-vindas** — versão do pacote; se detectou instalação: "Atualizar de vX para vY" ou "Nova instância".
2. **Pasta e instância** — pasta base, nome da instância, ambiente (`Production`).
3. **Banco de dados** — servidor SQL, autenticação (SQL ou Windows), usuário/senha, banco Common,
   banco da empresa; botão **Testar conexão**. Bancos de empresas adicionais vêm da tabela
   `COMPANIES` do Common (não são digitados).
4. **Modo ERP** — `STANDALONE` ou `SAPB1`; se SAPB1: banco SAP da empresa e `SBO-COMMON` (com teste).
5. **Empresa e portas** — nome da empresa, portas Gateway/Web/Reports (padrões 55000/55100/55200 —
   call do builder; pré-preenchidas na atualização), URL pública do Gateway (`Security:AppBaseUrl`
   e `Notifications:AppBaseUrl`), logo opcional (copiado para `Reports\wwwroot\images\logo.png`).
6. **Integrações (opcional, recolhível)** — SMTP (Gateway `Email`), WhatsApp, D4Sign
   (sandbox/produção), NF-e (`CertificateKey`, responsável técnico), Reinf software house, caminho do
   Chromium.
7. **Chaves novas** — só na atualização e só se houver: lista as chaves que existem no padrão da nova
   versão e não estavam no arquivo do cliente, com o valor padrão editável.
8. **Resumo** — o que será feito, passo a passo; senhas mascaradas.
9. **Execução** — barra de progresso + log ao vivo por passo.
10. **Concluído** — status de cada serviço, resultado do teste de endpoint, botão "Abrir no navegador",
    caminho do log.

Validação por página antes de Avançar (portas livres/distintas, conexão testada, campos obrigatórios).

## Configuração (levantamento 27/10)

Comum (perguntado uma vez, gravado nos 3): connection strings `SiagroDB`, `SiagroCommon`, e em SAPB1
`SapDB`, `SapCommon`; `Erp`; `CompanyName`.

| Serviço | Chaves geridas pelo assistente |
|---|---|
| Gateway | `Security:AppBaseUrl`, `Security:PasswordPolicy:*`, `Email:*`, `ReverseProxy:Clusters:backend/reports:Destinations:*:Address` (montadas das portas) |
| Web | `Reports:InternalBaseUrl` (= `http://localhost:<porta Reports>`), `Reports:InternalApiKey`, `Notifications:*`, `Signature:*`, `Nfe:*`, `Reinf:SoftwareHouse:*` |
| Reports | `Reports:InternalApiKey` (igual ao do Web), `CompanyLogoPath` |

`Reports:InternalApiKey`: gerada aleatoriamente na instalação nova; mantida na atualização.

Observado no levantamento: o Gateway da Yokotobi tem rotas (`hangfire`, `apiroute`) que o padrão não
tem. Rotas do YARP **não** são configuração de cliente: o assistente sempre usa `ReverseProxy:Routes`
do padrão da versão e remove `Routes` do arquivo do cliente na mesclagem (com backup), mantendo só os
endereços dos clusters. Se o padrão estiver sem rotas necessárias (`hangfire`, `/api`), corrigir o
`appsettings.json` do Gateway no repositório como parte deste trabalho.

## Mesclagem do appsettings (`AppSettingsMerger`)

Entrada: padrão novo (`appsettings.json` da versão), arquivo do cliente (`appsettings.<Ambiente>.json`),
valores das telas. Saída: novo arquivo do cliente + relatório de diferenças.

Regras:
1. Valor que existe no arquivo do cliente é mantido, salvo se o usuário o alterou numa tela.
2. Chave do padrão ausente no cliente: não é copiada para o arquivo do cliente (o padrão já a fornece
   em runtime pela cascata de configuração do ASP.NET Core), mas aparece na página "Chaves novas"; se o
   usuário alterar o valor, aí é gravada.
3. Chave do cliente que não existe mais no padrão: mantida e listada no log como "possivelmente obsoleta".
4. Exceção: `ReverseProxy:Routes` (ver acima).
5. Preserva ordem e formatação razoável (`System.Text.Json.Nodes`, indentado).
6. Backup do arquivo anterior sempre.

Casos de teste obrigatórios: instalação nova; atualização sem chave nova; com chave nova; com chave
obsoleta; usuário altera um valor existente; arrays (não mesclar item a item — array é valor atômico);
connection string com senha preservada sem exibição.

## Migrations

O pacote leva os bundles. Passo de migração (serviços parados):
1. `efbundle-common.exe --connection <SiagroCommon>`.
2. Ler `COMPANIES` no Common e rodar `efbundle-app.exe --connection <banco>` para **cada** banco de
   empresa (o `PendingMigrationsGuard` checa todos).
3. Antes de aplicar: listar pendentes e mostrar no Resumo ("3 migrations no Common, 5 em cada empresa").
4. O usuário SQL informado precisa de permissão de DDL; se o teste mostrar que não tem, o assistente
   oferece informar outro usuário só para a migração (não gravado em appsettings).
5. Falha na migration → para tudo, restaura binários e appsettings do backup e religa a versão
   anterior. **Banco não tem rollback automático**: o assistente avisa no Resumo que é recomendado ter
   backup do SQL e oferece um checkbox "Já fiz backup do banco" obrigatório antes de executar em
   atualização com migrations pendentes.

## Sequência de execução (atualização)

Verificar portas/conexões → parar Gateway, Reports, Web → backup das pastas → copiar binários
(preservando `appsettings.<Ambiente>.json`, `logs`, e o logo do cliente em `Reports\wwwroot\images`) →
gravar appsettings mesclados → migrations → (re)criar serviços com `binPath`
`"<exe>" --environment <Amb> --urls http://*:<porta>`, início automático, recuperação "reiniciar em
falha" → iniciar Web, Reports, Gateway → testar `http://localhost:<porta>/` de cada um (aceita 2xx–4xx
como "respondendo", igual aos scripts) → gravar `install.json`.

Instalação nova: mesma sequência sem backup/parada. Cada passo implementa `Rollback`; falha executa
os rollbacks dos passos já feitos em ordem inversa.

## Log

`logs\setup-*.log` com cada passo, comando e saída de `sc.exe`/bundles. Senhas e chaves mascaradas
(connection string: `Password=***`).

## Decisões da revisão (27/10)

- **Padrão sanitizado no build** (minha decisão): `build-installer.ps1` grava no pacote os
  `appsettings.json` com connection strings, senhas, tokens e chaves **vazios**. Os arquivos do
  repositório não mudam (setups de dev seguem funcionando). Assim um valor ausente no cliente nunca cai
  num banco de dev, e credencial nenhuma é distribuída. Validação na página de Banco garante que
  `SiagroDB`/`SiagroCommon` (e SAP, se SAPB1) foram preenchidos.
- **Bancos das empresas** (já respondido pelo código): `COMPANIES.ConnectionString` guarda a connection
  string completa de cada empresa; o `MigrationRunner` usa ela como está. O usuário alternativo de DDL,
  se informado, substitui só `User Id`/`Password` dessas strings (via `SqlConnectionStringBuilder`).
- **Pasta e nomes antigos** (minha decisão): a atualização reaproveita a pasta existente sem mover nada
  (`C:\IdxSistemas\SiagroB1\Producao\{Web,Gateway,Reports}` já casa com o layout, instância
  `Producao`) e mantém os nomes de serviço antigos. Instâncias novas usam o padrão novo; a mistura é aceita.

### Respostas do usuário (27/10) e arquivos reais da Yokotobi

- A configuração da produção **já** está em `appsettings.Production.json` de cada serviço; o
  `appsettings.json` não é usado. Não precisa passo de conversão: a primeira atualização só mescla.
- Portas reais: Gateway 55000, Web 55100, Reports 55200 (confirma os padrões do assistente).
- O Gateway de produção **não** tem rota `/api`. Ele sobrescreve `Routes` com só 4 rotas
  (`odata-metadata`, `odata-route`, `reports-route`, `hangfire`); como a configuração do ASP.NET Core
  mescla chave a chave, as rotas do padrão (`scales`, `truck-scale-ws`, `hooks`) continuam valendo. A
  regra "remover `Routes` do cliente" segue; `hangfire` precisa entrar no padrão do Gateway no repositório.
- Chaves que existem na produção e faltaram no levantamento: `SAPB1:ServiceLayerUrl` (Web; vai na
  página Modo ERP quando SAPB1) e `Security`/`Email` copiados também no arquivo do Web (sobra
  inofensiva; mantida pela regra 3).
- Arquivos reais (com senhas) para testes: usar cópias **mascaradas** como fixtures; nunca versionar os originais.
- Sem certificado de assinatura por enquanto: o pacote sai sem assinatura; `build-installer.ps1` aceita
  um parâmetro opcional de certificado (`signtool`) para quando houver.

## Deixado para o builder

- Visual do assistente (estilo WPF padrão do Windows é suficiente).
- Estrutura MVVM sem bibliotecas externas.
- Modo silencioso por linha de comando (`--silent --config arquivo.json`) — desejável, não obrigatório.
- Desinstalação: botão no assistente quando detecta instalação ("Remover serviços"), sem apagar pastas
  nem banco.

## Commits

Escopo de commit: `platform` (ex.: `feat(platform): adiciona assistente de instalação windows`).
