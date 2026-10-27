# Instalador Windows do SiagroB1

Assistente visual (Avançar → Avançar → Concluir) que instala e atualiza o **Web**, o **Gateway** (com o
frontend) e os **Relatórios** como serviços do Windows. Ele mescla o `appsettings.<Ambiente>.json` do
cliente, aplica as migrations e desfaz tudo se algo falhar.

Solução própria (`SiagroB1.Installer.sln`), fora do `SiagroB1.sln` de propósito: o WPF só compila no Windows.

## Pré-requisitos (máquina que gera o pacote)

- Windows com .NET SDK 10 (`dotnet --version`)
- `dotnet-ef` instalado (`dotnet tool install -g dotnet-ef`)
- Node + Yarn para o frontend
- `siagro-b1-frontend` clonado ao lado do backend (`..\siagro-b1-frontend`)
- Os dois repositórios no commit que vai ser entregue: o pacote leva o código que estiver na pasta,
  inclusive o que ainda não foi commitado
- Uns 5 GB livres e memória disponível; feche o que puder, porque o build publica 3 apps self-contained
  e 2 bundles de migration (leva ~10 min)

## Gerar o instalador

Na raiz do `siagro-b1-backend`:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Saída: `installer\out\SiagroB1-Setup-<versão>.exe` (~500 MB, arquivo único com tudo dentro).

Parâmetros opcionais:

| Parâmetro | Uso |
|---|---|
| `-Version 1.1.300` | Versão do pacote. Padrão: `version` do `package.json` do frontend. |
| `-FrontendPath D:\fe` | Outro caminho para o `siagro-b1-frontend`. |
| `-SkipFrontendBuild` | Usa o `dist\` que já existe, sem rodar `yarn build`. **Só para teste**: numa versão de verdade o `yarn build` é obrigatório (o cachebuster depende dele). |
| `-CertificateThumbprint ABC123` | Assina o `.exe` com `signtool` (certificado de assinatura de código instalado no Windows). |

### O que o script faz

1. `dotnet publish` de Web, Gateway e Reports (`win-x64`, self-contained).
2. `yarn build` do frontend e cópia do `dist\` para o `wwwroot` do Gateway.
3. Copia o `appsettings.json` padrão de cada serviço do repositório e **limpa** connection strings,
   senhas, tokens e chaves. Nenhum `appsettings.<Cliente>.json` entra no pacote.
4. Gera `efbundle-app.exe` e `efbundle-common.exe` e a lista das migrations de cada contexto.
5. Grava o `manifest.json` (versão, commits do back e do front, data).
6. Confere se não sobrou senha nos `appsettings` e para se encontrar alguma.
7. Compacta em `payload.zip` e embute no `.exe` do assistente. Se o pacote não couber como recurso
   embutido, gera uma pasta com o `.exe` e o `payload.zip` lado a lado. Distribua os dois juntos.

## Usar no servidor do cliente

1. Copie o `.exe` para o servidor e execute. Ele pede administrador. Sem assinatura digital, o
   SmartScreen avisa: clique em *Mais informações → Executar assim mesmo*.
2. Aponte a pasta (padrão `C:\IdxSistemas\SiagroB1\Producao`). Se já houver instalação, inclusive a
   feita pelos antigos `Install-SiagroB1-*-Service.ps1`, ela é detectada e as telas vêm preenchidas.
3. Confira banco (botão **Testar conexão**), modo ERP, portas (padrão 55000/55100/55200) e integrações.
4. No **Resumo**, faça backup dos bancos antes de marcar a confirmação. Migration aplicada não volta
   sozinha.
5. Clique em **Instalar**. Log em `<pasta>\logs\setup-<data>.log`; backup da versão anterior em
   `<pasta>\backups\<data>\`.

Para remover os serviços, use **Remover serviços** na tela de boas-vindas. Pastas e bancos ficam.

### Regras que o assistente segue

- A configuração do cliente fica em `appsettings.<Ambiente>.json` e nunca é sobrescrita: só recebe o
  que foi alterado nas telas. O `appsettings.json` é o padrão da versão e é sempre trocado.
- As rotas do YARP (`ReverseProxy:Routes`) sempre vêm do padrão; as do arquivo do cliente são descartadas.
- A cópia preserva `appsettings.*.json`, `logs\` e `Reports\wwwroot\images\logo.png` do cliente.
- Aplica as migrations no Common e em cada banco da tabela `COMPANIES`. Banco inexistente é erro: o
  instalador nunca cria banco.

## Desenvolvimento

```powershell
dotnet test installer\SiagroB1.Installer.sln
```

- Abrir o assistente sem gerar o pacote:
  `SiagroB1.Installer.exe --payload <pasta com Web\ Gateway\ Reports\ migrations\>`
  (por exemplo `installer\out\payload` depois de um build).
- Conferir as telas sem abrir o app: defina `SIAGRO_INSTALLER_SNAPSHOTS=<pasta>` e rode o
  `WindowRenderTests`. Ele salva um PNG por página.
- Desenho e decisões: `docs/superpowers/specs/2026-10-27-windows-installer-design.md`;
  verificação ponta a ponta: `docs/superpowers/plans/2026-10-27-windows-installer-verification.md`.
