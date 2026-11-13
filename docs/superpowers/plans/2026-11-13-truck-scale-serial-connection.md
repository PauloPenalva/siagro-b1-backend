# Balança: conexão do indicador por TCP/IP ou Serial — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada balança cadastrada escolhe se o `SiagroB1.Client` lê o indicador por TCP/IP (como hoje) ou pela porta serial.

**Architecture:** O cadastro `TRUCK_SCALES` ganha o tipo de conexão e os parâmetros da serial; o Web manda isso ao Client no `scale_config`. No Client, o laço de leitura sai de `ScaleTcpConnection` para uma peça sobre `Stream` (em `SiagroB1.Commons`, testável sem hardware) e dois transportes (TCP e Serial) abrem o `Stream`.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData, `System.IO.Ports`, xUnit; OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-11-13-truck-scale-serial-connection-design.md`

## Global Constraints

- Dois repositórios, dois worktrees, mesmo branch `feature/truck-scale-serial`:
  backend `C:\Projetos\SiagroB1\worktrees\truck-scale-serial\backend`, frontend `...\truck-scale-serial\frontend`.
  Conferir o branch antes de cada commit. **Nunca push, nunca merge em `main`.**
- Todo arquivo novo recebe `git add` logo após ser criado.
- Identificadores em inglês; texto de tela, mensagens de erro e comentários em pt-BR.
- Mensagem de commit: `tipo(weighing): descrição em pt-BR` + trailers `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>` e `Claude-Session: https://claude.ai/code/session_01GhxoakqAHtuUDLCFsdX3uy`. Commit com migration leva `DB: AddTruckScaleSerialConnection`.
- Balança TCP já cadastrada não muda de comportamento: `ConnectionType` padrão `Tcp` (valor 0).
- Padrões da serial quando o campo é nulo: baud 9600, 8 bits de dados, paridade nenhuma, 1 bit de parada.
- **Não aplicar a migration no banco local compartilhado** (ele tem migrations de outros branches). Verificação com banco só em cópia.
- Não matar processos por nome (`taskkill /IM`); só pelo PID que a própria tarefa iniciou.

## Review Focus

- Porta serial inexistente ou ocupada por outro programa → o Client não cai; a balança fica offline e tenta de novo a cada 3 s, com o erro no log. (Task 4)
- Cabo desconectado no meio da leitura (o `Stream` termina ou lança) → offline e reconexão, sem peso antigo sendo retransmitido como atual. (Tasks 3 e 4)
- Quadro partido entre duas leituras e lixo antes do primeiro terminador (comum em serial ao abrir no meio de um quadro) → nenhum peso errado emitido. (Task 3)
- Balança `Serial` salva sem porta, ou com bits de dados/parada fora do aceito → recusa com mensagem em pt-BR, tanto no POST quanto no PATCH. (Task 2)
- Client antigo recebendo `scale_config` com campos novos, e Client novo recebendo `scale_config` sem eles (servidor antigo) → ambos seguem em TCP. (Task 4)

---

### Task 1: Campos no cadastro e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/ScaleConnectionType.cs`, `SiagroB1.Domain/Enums/ScaleSerialParity.cs`
- Modify: `SiagroB1.Domain/Entities/TruckScale.cs`
- Create: migration `AddTruckScaleSerialConnection` em `SiagroB1.Migrations/AppContext/`

**Interfaces — Produces:**
- `enum ScaleConnectionType { Tcp, Serial }` e `enum ScaleSerialParity { None, Odd, Even }` (namespace `SiagroB1.Domain.Enums`).
- Em `TruckScale`: `ScaleConnectionType ConnectionType` (padrão `Tcp`), `string? SerialPortName` (`VARCHAR(50)`), `int? BaudRate`, `int? DataBits`, `ScaleSerialParity Parity` (padrão `None`), `int? StopBits`.

- [ ] **Step 1:** Criar os dois enums e acrescentar as seis propriedades em `TruckScale`, logo depois de `Port`, com um comentário pt-BR dizendo que nulo usa o padrão (9600/8/1).
- [ ] **Step 2:** Gerar a migration: `dotnet ef migrations add AddTruckScaleSerialConnection --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`.
- [ ] **Step 3:** Abrir a migration gerada. Ela deve conter **somente** seis `AddColumn` em `TRUCK_SCALES` (`ConnectionType` e `Parity` `int NOT NULL DEFAULT 0`; os demais anuláveis) e o `Down` simétrico. Se trouxer qualquer outra alteração, é sujeira do snapshot: remover a migration, não editar à mão, e reportar.
- [ ] **Step 4:** `dotnet build SiagroB1.sln` — 0 erros.
- [ ] **Step 5:** `git add` dos arquivos novos e commit `feat(weighing): acrescenta tipo de conexão e dados da serial à balança` com `DB: AddTruckScaleSerialConnection`.

### Task 2: Validação do cadastro e configuração enviada ao Client

**Files:**
- Create: `SiagroB1.Application/Services/TruckScaleValidator.cs`
- Modify: `SiagroB1.Application/Services/TruckScaleService.cs` (`CreateAsync`, `UpdateAsync`)
- Modify: `SiagroB1.Web/Sockets/TruckScale/ScaleMessages.cs` (`ScaleConfigPayload`), `SiagroB1.Web/Sockets/TruckScale/ScaleConfigProvider.cs`
- Test: `SiagroB1.Application.Tests/Scales/TruckScaleValidatorTests.cs`, `SiagroB1.Application.Tests/Scales/ScaleConfigProviderTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `static void TruckScaleValidator.Validate(TruckScale scale)` — lança `DefaultException`.
  `ScaleConfigPayload` ganha `string ConnectionType` (`"Tcp"`/`"Serial"`), `string? SerialPortName`, `int BaudRate`, `int DataBits`, `string Parity` (`"None"`/`"Odd"`/`"Even"`), `int StopBits`. Serializados em camelCase pelo endpoint existente.

- [ ] **Step 1: Testes que falham** em `TruckScaleValidatorTests`:
  - `Tcp_sem_dados_de_serial_passa` (inclusive com `IpAddress` nulo — legado não trava).
  - `Serial_sem_porta_recusa` → mensagem `"Informe a porta serial da balança."` (porta nula, vazia e só espaços).
  - `Serial_com_porta_e_campos_nulos_passa`.
  - `Serial_bits_de_dados_invalido_recusa` (`DataBits = 9`) → `"Bits de dados deve ser 7 ou 8."`
  - `Serial_bits_de_parada_invalido_recusa` (`StopBits = 3`) → `"Bits de parada deve ser 1 ou 2."`
  - `Serial_baud_rate_nao_positivo_recusa` (`BaudRate = 0`) → `"Baud rate deve ser maior que zero."`
- [ ] **Step 2: Testes que falham** em `ScaleConfigProviderTests` (AppDbContext InMemory, no padrão dos outros testes do projeto):
  - balança TCP → `ConnectionType == "Tcp"`, `Ip`/`Port` como cadastrados.
  - balança Serial com campos nulos → `"Serial"`, `SerialPortName` do cadastro, `BaudRate == 9600`, `DataBits == 8`, `Parity == "None"`, `StopBits == 1`.
  - balança Serial com 4800/7/Even/2 → esses valores.
- [ ] **Step 3:** Rodar `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~TruckScaleValidatorTests|FullyQualifiedName~ScaleConfigProviderTests"` — falham por símbolo inexistente.
- [ ] **Step 4:** Implementar `TruckScaleValidator.Validate` (regras só valem quando `ConnectionType == Serial`) e chamá-lo no início de `CreateAsync` e de `UpdateAsync` — o PATCH e o PUT passam por `UpdateAsync`.
- [ ] **Step 5:** Acrescentar as propriedades em `ScaleConfigPayload` e preenchê-las em `ScaleConfigProvider.GetAsync`, resolvendo os padrões.
- [ ] **Step 6:** Rodar o filtro do Step 3 — passam. Rodar `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Scales"` — tudo verde.
- [ ] **Step 7:** Commit `feat(weighing): valida e envia ao client a conexão serial da balança`.

### Task 3: Laço de leitura sobre `Stream`

**Files:**
- Create: `SiagroB1.Commons/Scales/ScaleStreamReader.cs`
- Test: `SiagroB1.Application.Tests/Scales/ScaleStreamReaderTests.cs`

**Interfaces — Produces:**

```csharp
public static class ScaleStreamReader
{
    /// Lê até o fluxo terminar (ReadAsync devolve 0) ou o token cancelar.
    public static Task ReadAsync(
        Stream stream,
        ScaleProtocolOptions options,
        Action<int> onWeight,
        Action<string>? onRawFrame,
        CancellationToken ct);
}
```

É a extração, sem mudança de comportamento, do miolo de `ScaleTcpConnection.RunAsync` (buffer de 1024, `Encoding.ASCII`, `ScaleFrameBuffer(options.FrameTerminator)`, `ScaleProtocolFactory.Create(options)`). Exceções do `Stream` propagam; `OperationCanceledException` propaga.

- [ ] **Step 1: Testes que falham**, usando o preset padrão (`new ScaleProtocolOptions()`) e quadros válidos tirados de `ScaleProtocolTests`:
  - `Emite_um_peso_por_quadro_completo` — dois quadros num `MemoryStream` → dois pesos, na ordem.
  - `Junta_quadro_partido_entre_leituras` — um `Stream` de teste que entrega o quadro em dois pedaços → um peso só.
  - `Ignora_lixo_antes_do_primeiro_terminador` — meio quadro + terminador + quadro válido → só o peso do válido.
  - `Termina_quando_o_fluxo_acaba` — a `Task` completa sem exceção ao fim do `MemoryStream`.
  - `Repassa_o_quadro_cru_quando_pedido` — `onRawFrame` recebe cada quadro; com `null` não quebra.
  - `Cancelamento_interrompe_a_leitura` — `Stream` que nunca devolve dados + token cancelado → `OperationCanceledException`.
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ScaleStreamReaderTests"` — falha de compilação.
- [ ] **Step 3:** Implementar `ScaleStreamReader.ReadAsync`.
- [ ] **Step 4:** Rodar o filtro — passam.
- [ ] **Step 5:** Commit `refactor(weighing): extrai a leitura de quadros do indicador para um stream`.

### Task 4: Transportes TCP e Serial no Client

**Files:**
- Modify: `SiagroB1.Client/SiagroB1.Client.csproj` (pacote `System.IO.Ports`, versão 10.0.x alinhada aos demais pacotes `Microsoft.Extensions.*`)
- Create: `SiagroB1.Client/Readers/IScaleTransport.cs`, `TcpScaleTransport.cs`, `SerialScaleTransport.cs`, `ScaleIndicatorConnection.cs`
- Delete: `SiagroB1.Client/Readers/ScaleTcpConnection.cs` (substituído por `ScaleIndicatorConnection` + `TcpScaleTransport`)
- Modify: `SiagroB1.Client/Dtos/ScaleConfigMessage.cs`, `SiagroB1.Client/ScaleWorker.cs`, `SiagroB1.Client/README.md`, `CLAUDE.md` (a frase "Client … reading truck-scale weight data over TCP")
- Test: `SiagroB1.Application.Tests/Scales/ScaleConfigDataTests.cs` **somente se** o projeto de testes puder referenciar `SiagroB1.Client` sem conflito; senão, a regra de escolha do transporte vai para um método estático em `SiagroB1.Commons/Scales` e é testada lá. Decidir pelo que compilar, e registrar no relatório.

**Interfaces:**
- Consumes: `ScaleStreamReader.ReadAsync` (Task 3); nomes camelCase do payload (Task 2).
- Produces:

```csharp
public interface IScaleTransport
{
    string Description { get; }                       // "192.168.0.10:4001" ou "COM3 9600 8N1", para o log
    Task<Stream> OpenAsync(CancellationToken ct);     // o chamador descarta o Stream ao terminar
}
```

`ScaleIndicatorConnection(IScaleTransport transport, ScaleProtocolOptions options, bool logRawFrames, Action<int> onWeight, Action<bool> onConnectionChanged, ILogger logger)` com `Task RunAsync(CancellationToken ct)` — mesmo ciclo do `ScaleTcpConnection` atual: abre, `onConnectionChanged(true)`, lê com `ScaleStreamReader`, em qualquer saída `onConnectionChanged(false)` e espera 3 s.

`ScaleConfigData` ganha `ConnectionType` (padrão `"Tcp"`), `SerialPortName`, `BaudRate` (9600), `DataBits` (8), `Parity` (`"None"`), `StopBits` (1).

- [ ] **Step 1: Teste que falha** — a escolha do transporte: `connectionType` ausente ou `"Tcp"` → TCP; `"Serial"` (qualquer caixa) → Serial; valor desconhecido → TCP. E a desserialização de um `scale_config` **sem** os campos novos mantém os padrões acima.
- [ ] **Step 2:** Rodar — falha.
- [ ] **Step 3:** Implementar `IScaleTransport`, `TcpScaleTransport(host, port)` e `SerialScaleTransport(portName, baudRate, dataBits, parity, stopBits)`. O serial abre `SerialPort` e devolve um `Stream` que, ao ser descartado, fecha a porta. `ReadTimeout` infinito; sem controle de fluxo.
- [ ] **Step 4:** Implementar `ScaleIndicatorConnection`, trocar o uso em `ScaleWorker.CreateReader` e apagar `ScaleTcpConnection.cs` (`git rm`). O ramo `UseMockScale` não muda.
- [ ] **Step 5:** Garantir no `ScaleWorker.StreamWeightAsync` que, ao voltar de offline, o primeiro `weight_tick` só sai depois de um peso novo lido (zerar `_lastWeight` quando `_indicatorOnline` vira `false`) — sem peso antigo retransmitido como atual. Cobrir com teste se a lógica puder ser isolada; senão, verificar na Task 6.
- [ ] **Step 6:** Atualizar o `README.md` do Client: seção "Conexão do indicador" explicando TCP/IP × Serial, que tudo vem do cadastro, e que o usuário do serviço Windows precisa de acesso à porta COM. Corrigir a frase do `CLAUDE.md`.
- [ ] **Step 7:** `dotnet build SiagroB1.sln` — 0 erros; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Scales"` — verde; `dotnet publish SiagroB1.Client -c Release -r win-x64 --self-contained -o <pasta temporária>` — conclui e a pasta contém `System.IO.Ports.dll`.
- [ ] **Step 8:** Commit `feat(weighing): lê o indicador da balança pela porta serial`.

### Task 5: Tela do cadastro (repositório do frontend)

**Files:**
- Modify: `webapp/view/truckScales/fragments/Form.fragment.xml`
- Modify, se necessário: `webapp/controller/truckScales/Add.controller.ts` (valor inicial `ConnectionType: "Tcp"`, `Parity: "None"`)

Antes de editar, consultar as diretrizes de UI5 (`ui5:ui5-best-practices` e o `ui5-mcp-server`). Manter o `SimpleForm` existente — não reescrever o formulário.

- [ ] **Step 1:** Em "Conexão", antes de "Endereço IP", acrescentar o `Select` **Tipo de conexão** (`selectedKey="{ path: 'ConnectionType', targetType: 'any' }"`, itens `Tcp` → "TCP/IP" e `Serial` → "Serial"), no mesmo padrão do `Select` de Protocolo.
- [ ] **Step 2:** "Endereço IP" e "Porta" (rótulo e campo) ficam visíveis só quando `ConnectionType !== 'Serial'`. Usar expression binding com `targetType: 'any'` e sem o caractere `<` dentro do XML.
- [ ] **Step 3:** Visíveis só quando `ConnectionType === 'Serial'`:
  - **Porta serial** — `Input value="{SerialPortName}"`, `maxLength="50"`, `placeholder="COM3"`.
  - **Baud rate** — `Select` com 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200; vazio = 9600.
  - **Bits de dados** — `Select` 7 / 8; vazio = 8.
  - **Paridade** — `Select` `None` → "Nenhuma", `Odd` → "Ímpar", `Even` → "Par".
  - **Bits de parada** — `Select` 1 / 2; vazio = 1.
  Os campos numéricos são `Edm.Int32` anuláveis: conferir que o `Select` grava número (não string) e que "vazio" grava `null`.
- [ ] **Step 4:** `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` — sem erro novo.
- [ ] **Step 5:** Commit no repositório do frontend: `feat(weighing): escolhe tcp/ip ou serial no cadastro da balança`.

### Task 6: Verificação de ponta a ponta

- [ ] **Step 1:** Criar uma **cópia** de um banco local de empresa STANDALONE (perfil `ceagui`) e aplicar nela a migration; conferir as seis colunas e que as balanças existentes ficaram `ConnectionType = 0`.
- [ ] **Step 2:** Subir Web + Gateway apontando para a cópia e o frontend (`yarn start:dev` com `node_modules` próprio do worktree).
- [ ] **Step 3:** Pela tela: criar uma balança TCP e uma Serial; alternar o tipo e ver os campos trocarem; salvar Serial sem porta e ver a mensagem de recusa; reabrir e conferir que os valores voltaram.
- [ ] **Step 4:** Caminho TCP sem regressão: um emissor TCP local de quadros BJ850 + Client com a balança TCP → peso ao vivo na tela de pesagem.
- [ ] **Step 5:** Caminho Serial: se houver par de portas virtuais (com0com) na máquina, emitir quadros numa ponta e ler na outra pelo Client. Se não houver, registrar que o transporte serial ficou verificado só até a abertura da porta (porta inexistente → offline + nova tentativa a cada 3 s no log) e que a leitura real fica para o PC da balança, com `LogRawFrames` ligado.
- [ ] **Step 6:** Derrubar o emissor no meio e conferir offline na tela e reconexão ao religar.
- [ ] **Step 7:** Rodar a suíte completa `dotnet test SiagroB1.Application.Tests` e `yarn test` no frontend; reportar os números reais.
- [ ] **Step 8:** Encerrar só os processos iniciados (pelo PID) e remover a cópia do banco.
