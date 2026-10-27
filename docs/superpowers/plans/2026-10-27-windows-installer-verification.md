# Verificação ponta a ponta do instalador — 27/10/2026

Máquina de desenvolvimento, instância `Teste` em `C:\IdxSistemas\SiagroB1\Teste`, portas 56000/56100/56200,
bancos `IDX_SIAGRO_COMMON_E2E` e `IDX_SIAGRO_DEV_E2E` (cópias por backup/restore; `COMPANIES` da cópia
reapontado para a cópia). Pacote: `build-installer.ps1 -SkipFrontendBuild` (v1.1.294, be 071d4f64).

Conduzido por um programa auxiliar (fora do repositório) que usa as ViewModels reais do assistente
(Pasta → Banco → ERP → Portas → Integrações → Chaves novas → Resumo → Execução) com serviços Windows e
migrations reais, rodando elevado. As telas em si foram conferidas pelo `WindowRenderTests`.

| Cenário | Resultado |
|---|---|
| Instalação nova | 3 serviços `siagrob1-teste-*` rodando; Gateway 200; `efbundle-common` e `efbundle-app` rodaram de verdade ("No migrations were applied"). |
| Instalação legada (scripts antigos, sem `install.json`) → atualização | Detectada como legada, nomes antigos mantidos, banco/portas/URL pré-preenchidos; backup feito; serviços religados; Gateway 200. `appsettings.Production.json` do Web idêntico (valor só do cliente preservado); do Gateway saíram só as `Routes`. |
| Rollback (empresa apontando para banco inexistente) | Falha clara com o nome do banco; pasta restaurada do backup (marcador da versão anterior de volta); arquivo do cliente intacto; banco **não** criado. |

## Defeitos achados e corrigidos (commit ed87c585)

1. **O bundle do EF cria o banco se ele não existir** — com o nome errado em `COMPANIES` a atualização
   criou `NAO_EXISTE_E2E`, aplicou as 348 migrations e terminou "com sucesso". Agora o instalador
   recusa banco inexistente antes de rodar o bundle.
2. **Rollback parava no primeiro serviço que não subia** — Gateway e Reports ficavam parados porque o
   Web falhou. Agora tenta religar todos e lista as falhas.
3. **Tela "Chaves novas" listava dezenas de chaves** — comparava com o arquivo do cliente (que só tem o
   que ele mudou). Agora compara com o `appsettings.json` padrão já instalado; na atualização seguinte
   mostrou "nenhuma".

Limpeza: serviços de teste removidos, pasta `Teste`, bancos `*_E2E` e `.bak` apagados. Os serviços de
desenvolvimento pré-existentes (`siagrob1webservice` etc., em `C:\IdxSistemas\SiagroB1\{Web,...}`) não
foram tocados.
