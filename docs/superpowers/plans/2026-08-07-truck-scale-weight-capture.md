# Captura de peso da balança rodoviária — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Concluir a captura do peso da balança rodoviária de ponta a ponta — o `SiagroB1.Client` transmite o peso do indicador Jundiaí BJ850 continuamente, o servidor calcula estabilidade e emite comprovantes de captura, e as telas de 1ª/2ª pesagem mostram o peso ao vivo, com digitação manual restrita por permissão.

**Architecture:** O Client deixa de ser passivo e vira produtor: mantém uma conexão WebSocket por balança com o `SiagroB1.Web`, recebe a configuração (IP/porta/protocolo) do servidor e empurra `weight_tick`. O Web é dono da leitura corrente (`LiveReadingStore`), da estabilidade (`StabilityDetector`) e dos comprovantes (`CaptureStore`), publicando o peso ao navegador por SSE em `/scales/{code}/live`. As ações OData de pesagem passam a exigir comprovante de quem não tem `WEIGHING_MANUAL_ENTRY` e a validar a tara do caminhão.

**Tech Stack:** .NET 10 (OData 8, YARP, EF Core, xUnit + EF InMemory), OpenUI5 1.141 + TypeScript, SQL Server.

**Spec:** `docs/superpowers/specs/2026-08-07-truck-scale-weight-capture-design.md`

## Global Constraints

- **Nomenclatura:** identificadores de código, entidades, tabelas e colunas **sempre em inglês**; apenas texto que o usuário lê (labels, menus, mensagens de erro de negócio) em pt-BR. Comentários em pt-BR.
- **Nunca commitar nem fazer push.** Onde este plano diz "Stage", execute apenas `git add` no sub-repo correto (`siagro-b1-backend/` ou `siagro-b1-frontend/`). Os commits são feitos manualmente pelo usuário. Todo arquivo novo deve ser staged assim que criado.
- **Dois repositórios independentes:** `siagro-b1-backend/` e `siagro-b1-frontend/`. Nenhuma alteração atravessa os dois num mesmo `git add`.
- **Enums** são persistidos como `int` no banco e expostos como **string** no EDM do OData. No UI5, todo binding de enum precisa de `targetType: 'any'`.
- **Peso é sempre inteiro em quilos** (`int`). Nunca use `Decimal` em binding editável do UI5 — o parse vira string e o backend devolve 400 sem nomear o campo.
- **Migrations:** aplicar com `dotnet ef database update` passando o ambiente explicitamente. Nunca rodar com o perfil `db-migration` sem `ASPNETCORE_ENVIRONMENT` definido.
- **Comandos de build/teste** (a partir de `siagro-b1-backend/`):
  - `dotnet build SiagroB1.sln`
  - `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`
  - `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~NomeDaClasse`
- **Frontend** (a partir de `siagro-b1-frontend/`): `yarn ts-typecheck` e `yarn lint`. **Não** use `yarn test` como gate: o limiar de cobertura é 50% contra ~2,4% reais e ele falha sempre, independentemente da sua mudança.

## Estrutura de arquivos

### Novos — `SiagroB1.Commons/Scales/` (lógica pura, testável pelo `SiagroB1.Application.Tests`, que já referencia `Application` → `Commons`)

| Arquivo | Responsabilidade |
|---|---|
| `ScaleReading.cs` | Registro imutável de uma leitura `(Weight, Timestamp)` e do resultado `LiveWeight (Weight, Stable, Online)`. |
| `ScaleProtocolOptions.cs` | Parâmetros de parsing de um modelo de balança (preset + sobrescritas do cadastro). |
| `IScaleProtocol.cs` | Contrato de parsing de um frame em quilos. |
| `FixedPositionScaleProtocol.cs` | Preset Jundiaí BJ850: posição/tamanho fixos. |
| `RegexScaleProtocol.cs` | Protocolo genérico por expressão regular. |
| `ScaleProtocolFactory.cs` | Escolhe a implementação a partir das opções. |
| `ScaleFrameBuffer.cs` | Acumula bytes do TCP e devolve frames completos por terminador. |
| `StabilityDetector.cs` | Decide se o peso está estável dentro de uma janela. |
| `LiveReadingStore.cs` | Leitura corrente por balança, thread-safe. |
| `CaptureStore.cs` | Comprovantes de captura: criação, consumo único, expiração. |

### Novos — backend

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Enums/ScaleProtocolType.cs` | `JundiaiBj850` \| `Generic`. |
| `SiagroB1.Domain/Enums/WeighingScalePurpose.cs` | `Opening` \| `Closing`. |
| `SiagroB1.Domain/Entities/UserTruckScale.cs` | Vínculo usuário × balança × finalidade. |
| `SiagroB1.Application/Interfaces/IUserPermissions.cs` | Contrato de leitura de permissões efetivas. |
| `SiagroB1.Application/Services/Security/UserPermissionsService.cs` | Implementação sobre o `CommonDbContext`. |
| `SiagroB1.Application/Services/UserTruckScales/*.cs` | CRUD de `USER_TRUCK_SCALES`. |
| `SiagroB1.Web/Controllers/UserTruckScalesController.cs` | Entity set OData. |
| `SiagroB1.Web/Sockets/TruckScale/TruckScaleHub.cs` | Substitui o `TruckScaleWebSocketConnectionManager`. |
| `SiagroB1.Web/Sockets/TruckScale/ScaleConfigProvider.cs` | Lê `TRUCK_SCALES` e monta o `scale_config`. |
| `SiagroB1.Web/Controllers/ScalesController.cs` | SSE `/scales/{code}/live` e `POST /scales/{code}/capture`. |
| `SiagroB1.Client/Readers/ScaleTcpConnection.cs` | Socket TCP + buffer + protocolo, com callbacks. |
| `SiagroB1.Client/ScaleWorker.cs` | Um worker por balança: WS, config, streaming. |
| `SiagroB1.Client/Dtos/ScaleConfigMessage.cs` | Contrato das mensagens WS. |

### Modificados — backend

- `SiagroB1.Domain/Entities/TruckScale.cs` — conexão, parsing, tara.
- `SiagroB1.Domain/Entities/Truck.cs` — `TareWeight`.
- `SiagroB1.Domain/Entities/WeighingTicket.cs` — auditoria da origem do peso.
- `SiagroB1.Infra/Context/AppDbContext.cs` — `DbSet<UserTruckScale>`.
- `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` — entity set e parâmetros novos das ações.
- `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` — registros.
- `SiagroB1.Web/Program.cs` — pipeline.
- `SiagroB1.Web/Sockets/TruckScale/TruckScaleWebSocketEndpoint.cs` — novo protocolo de mensagens.
- `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsFirstWeighingService.cs` e `...SecondWeighingService.cs` — permissão, comprovante, tara.
- `SiagroB1.Web/Actions/WeighingTickets/WeighingTicketsFirst/SecondWeighingController.cs` — `CaptureId`.
- `SiagroB1.Security/Dtos/UserInfo.cs`, `SiagroB1.Security/Services/AuthService.cs` — `Permissions`.
- `SiagroB1.Gateway/appsettings*.json` — rota `/scales`.
- `SiagroB1.Client/Program.cs`, `Worker.cs`, `Readers/TcpScaleReader.cs`, `Mock/MockScaleReader.cs`, `appsettings*.json`.

### Removidos — backend

- `SiagroB1.Web/Sockets/TruckScale/TruckScaleCaptureController.cs`
- `SiagroB1.Web/Sockets/TruckScale/TruckScaleWebSocketConnectionManager.cs`
- `SiagroB1.Web/Sockets/PendingRequestStore.cs`
- `SiagroB1.Web/Sockets/WsMessageHandler.cs`

### Frontend

| Arquivo | Ação |
|---|---|
| `webapp/services/ScaleLiveService.ts` | Criar — wrapper do `EventSource`. |
| `webapp/types/ScaleLive.ts` | Criar — tipos do peso ao vivo e da captura. |
| `webapp/view/weighingTicket/fragments/WeighingCapture.fragment.xml` | Criar — bloco de captura compartilhado. |
| `webapp/controller/weighingTicket/GenericController.ts` | Modificar — ciclo de vida da captura. |
| `webapp/view/weighingTicket/fragments/FirstWeighingForm.fragment.xml`, `SecondWeighingForm.fragment.xml`, `Weighing.fragment.xml` | Modificar — usar o fragmento novo. |
| `webapp/controller/weighingTicket/FirstWeighing.controller.ts`, `SecondWeighing.controller.ts`, `Main.controller.ts` | Modificar — enviar `CaptureId`. |
| `webapp/view/truckScales/fragments/Form.fragment.xml` | Modificar — conexão, protocolo, tara. |
| `webapp/view/veiculo/fragments/Form.fragment.xml` | Modificar — tara. |
| `webapp/view/users/fragments/TruckScales.fragment.xml` | Criar — grade de balanças do usuário. |
| `webapp/view/users/Edit.view.xml`, `webapp/controller/users/Edit.controller.ts` | Modificar — aba nova. |
| `webapp/services/SessionService.ts`, `webapp/types/UserIdentity.ts` | Modificar — permissões. |
| `webapp/model/ServerRoutes.ts` | Modificar — rotas novas. |
| `ui5.yaml` | Modificar — proxy `/scales`. |

---

### Task 1: Parser de frame da balança

Lógica pura, sem I/O. É a parte que será calibrada em campo contra o BJ850 real, então precisa estar coberta por testes antes de qualquer fio ser ligado.

**Files:**
- Create: `SiagroB1.Commons/Scales/ScaleReading.cs`
- Create: `SiagroB1.Commons/Scales/ScaleProtocolOptions.cs`
- Create: `SiagroB1.Commons/Scales/IScaleProtocol.cs`
- Create: `SiagroB1.Commons/Scales/FixedPositionScaleProtocol.cs`
- Create: `SiagroB1.Commons/Scales/RegexScaleProtocol.cs`
- Create: `SiagroB1.Commons/Scales/ScaleProtocolFactory.cs`
- Create: `SiagroB1.Commons/Scales/ScaleFrameBuffer.cs`
- Test: `SiagroB1.Application.Tests/Scales/ScaleProtocolTests.cs`
- Test: `SiagroB1.Application.Tests/Scales/ScaleFrameBufferTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `record ScaleReading(int Weight, DateTime Timestamp)`
  - `record LiveWeight(int Weight, bool Stable, bool Online)`
  - `class ScaleProtocolOptions { string Protocol; int FramePrefixLength; int WeightLength; int DecimalPlaces; string FrameTerminator; string? FramePattern }`
  - `interface IScaleProtocol { bool TryParse(string frame, out int weightKg); }`
  - `static IScaleProtocol ScaleProtocolFactory.Create(ScaleProtocolOptions options)`
  - `class ScaleFrameBuffer { ScaleFrameBuffer(string terminator, int maxLength = 4096); IEnumerable<string> Append(string chunk); }`

- [x] **Step 1: Escrever os testes do parser (falhando)**

Crie `SiagroB1.Application.Tests/Scales/ScaleProtocolTests.cs`:

```csharp
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleProtocolTests
{
    private static ScaleProtocolOptions Bj850() => new();

    [Fact]
    public void Bj850_parses_the_six_digits_after_the_prefix()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.True(protocol.TryParse("=012345", out var weight));
        Assert.Equal(12345, weight);
    }

    [Fact]
    public void Bj850_ignores_a_trailing_carriage_return()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.True(protocol.TryParse("=012345\r", out var weight));
        Assert.Equal(12345, weight);
    }

    [Fact]
    public void Bj850_rejects_a_truncated_frame()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.False(protocol.TryParse("=0123", out _));
    }

    [Fact]
    public void Bj850_rejects_a_frame_with_non_digits_in_the_weight()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.False(protocol.TryParse("=01A345", out _));
    }

    [Fact]
    public void Bj850_reads_a_negative_weight()
    {
        var protocol = ScaleProtocolFactory.Create(Bj850());

        Assert.True(protocol.TryParse("=-01234", out var weight));
        Assert.Equal(-1234, weight);
    }

    [Fact]
    public void Decimal_places_are_rounded_to_whole_kilos()
    {
        var protocol = ScaleProtocolFactory.Create(new ScaleProtocolOptions { DecimalPlaces = 1 });

        Assert.True(protocol.TryParse("=012345", out var weight));
        Assert.Equal(1235, weight);
    }

    [Fact]
    public void Prefix_and_length_overrides_are_honoured()
    {
        var options = new ScaleProtocolOptions { FramePrefixLength = 3, WeightLength = 5 };
        var protocol = ScaleProtocolFactory.Create(options);

        Assert.True(protocol.TryParse("STX09876kg", out var weight));
        Assert.Equal(9876, weight);
    }

    [Fact]
    public void Generic_protocol_extracts_the_named_group()
    {
        var options = new ScaleProtocolOptions
        {
            Protocol = "Generic",
            FramePattern = @"PESO:\s*(?<weight>-?\d+)"
        };
        var protocol = ScaleProtocolFactory.Create(options);

        Assert.True(protocol.TryParse("PESO: 24680 KG", out var weight));
        Assert.Equal(24680, weight);
    }

    [Fact]
    public void Generic_protocol_rejects_a_frame_without_a_match()
    {
        var options = new ScaleProtocolOptions
        {
            Protocol = "Generic",
            FramePattern = @"PESO:\s*(?<weight>-?\d+)"
        };
        var protocol = ScaleProtocolFactory.Create(options);

        Assert.False(protocol.TryParse("SEM LEITURA", out _));
    }

    [Fact]
    public void Empty_frames_are_rejected_by_both_protocols()
    {
        Assert.False(ScaleProtocolFactory.Create(Bj850()).TryParse("", out _));
        Assert.False(ScaleProtocolFactory
            .Create(new ScaleProtocolOptions { Protocol = "Generic", FramePattern = @"(\d+)" })
            .TryParse("   ", out _));
    }
}
```

Crie `SiagroB1.Application.Tests/Scales/ScaleFrameBufferTests.cs`:

```csharp
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class ScaleFrameBufferTests
{
    [Fact]
    public void Returns_complete_frames_only()
    {
        var buffer = new ScaleFrameBuffer("\n");

        Assert.Empty(buffer.Append("=0123"));
        Assert.Equal(["=012345"], buffer.Append("45\n").ToArray());
    }

    [Fact]
    public void Returns_every_frame_present_in_a_single_chunk()
    {
        var buffer = new ScaleFrameBuffer("\n");

        Assert.Equal(["=000100", "=000200"], buffer.Append("=000100\n=000200\n").ToArray());
    }

    [Fact]
    public void Keeps_the_incomplete_tail_for_the_next_chunk()
    {
        var buffer = new ScaleFrameBuffer("\n");

        Assert.Equal(["=000100"], buffer.Append("=000100\n=0002").ToArray());
        Assert.Equal(["=000200"], buffer.Append("00\n").ToArray());
    }

    [Fact]
    public void Discards_the_buffer_when_no_terminator_ever_arrives()
    {
        var buffer = new ScaleFrameBuffer("\n", maxLength: 16);

        Assert.Empty(buffer.Append(new string('x', 32)));
        Assert.Equal(["=000100"], buffer.Append("=000100\n").ToArray());
    }
}
```

- [x] **Step 2: Rodar os testes e confirmar que falham**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~Scales`
Expected: falha de compilação — `ScaleProtocolFactory` e `ScaleFrameBuffer` não existem.

- [x] **Step 3: Implementar os tipos base**

`SiagroB1.Commons/Scales/ScaleReading.cs`:

```csharp
namespace SiagroB1.Commons.Scales;

/// <summary>Uma leitura crua do indicador, em quilos inteiros.</summary>
public sealed record ScaleReading(int Weight, DateTime Timestamp);

/// <summary>O que a tela precisa saber sobre a balança neste instante.</summary>
public sealed record LiveWeight(int Weight, bool Stable, bool Online);
```

`SiagroB1.Commons/Scales/ScaleProtocolOptions.cs`:

```csharp
namespace SiagroB1.Commons.Scales;

/// <summary>
/// Parâmetros de leitura de um modelo de balança. Os valores padrão são o preset do Jundiaí
/// BJ850 (ASCII contínuo, terminador CR/LF, seis dígitos a partir da posição 1); o cadastro da
/// balança sobrescreve o que for diferente, sem exigir recompilação.
/// </summary>
public sealed class ScaleProtocolOptions
{
    public string Protocol { get; init; } = "JundiaiBj850";

    public int FramePrefixLength { get; init; } = 1;

    public int WeightLength { get; init; } = 6;

    public int DecimalPlaces { get; init; }

    public string FrameTerminator { get; init; } = "\n";

    /// <summary>Expressão regular do protocolo genérico. O peso sai do grupo "weight" ou do grupo 1.</summary>
    public string? FramePattern { get; init; }
}
```

`SiagroB1.Commons/Scales/IScaleProtocol.cs`:

```csharp
namespace SiagroB1.Commons.Scales;

public interface IScaleProtocol
{
    /// <summary>Converte um frame já delimitado em quilos inteiros. Devolve false para lixo.</summary>
    bool TryParse(string frame, out int weightKg);
}
```

- [x] **Step 4: Implementar os dois protocolos e a fábrica**

`SiagroB1.Commons/Scales/FixedPositionScaleProtocol.cs`:

```csharp
using System.Globalization;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Peso em posição e tamanho fixos dentro do frame - o formato do Jundiaí BJ850 e da maioria dos
/// indicadores nacionais. O caractere de sinal, quando existe, ocupa a primeira posição do campo.
/// </summary>
public sealed class FixedPositionScaleProtocol(ScaleProtocolOptions options) : IScaleProtocol
{
    public bool TryParse(string frame, out int weightKg)
    {
        weightKg = 0;

        var clean = frame.Trim('\r', '\n', '\0', ' ');

        if (clean.Length < options.FramePrefixLength + options.WeightLength)
            return false;

        var field = clean.Substring(options.FramePrefixLength, options.WeightLength);

        var negative = field.StartsWith('-');
        var digits = negative ? field[1..] : field;

        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
            return false;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
            return false;

        weightKg = Scale(raw, negative, options.DecimalPlaces);
        return true;
    }

    /// <summary>
    /// O peso trafega e é gravado em quilos inteiros: aplicar as casas decimais aqui é o que
    /// permite comparar o comprovante de captura com o valor da ação por igualdade exata.
    /// </summary>
    internal static int Scale(long raw, bool negative, int decimalPlaces)
    {
        var divisor = Math.Pow(10, decimalPlaces);
        var value = (int)Math.Round(raw / divisor, MidpointRounding.AwayFromZero);

        return negative ? -value : value;
    }
}
```

`SiagroB1.Commons/Scales/RegexScaleProtocol.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace SiagroB1.Commons.Scales;

/// <summary>Protocolo genérico, para o próximo modelo de balança que não couber no de posição fixa.</summary>
public sealed class RegexScaleProtocol : IScaleProtocol
{
    private readonly Regex _pattern;
    private readonly int _decimalPlaces;

    public RegexScaleProtocol(ScaleProtocolOptions options)
    {
        var pattern = string.IsNullOrWhiteSpace(options.FramePattern)
            ? @"(?<weight>-?\d+)"
            : options.FramePattern;

        _pattern = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
        _decimalPlaces = options.DecimalPlaces;
    }

    public bool TryParse(string frame, out int weightKg)
    {
        weightKg = 0;

        var match = _pattern.Match(frame ?? string.Empty);
        if (!match.Success)
            return false;

        var group = match.Groups["weight"].Success ? match.Groups["weight"] : match.Groups[1];
        if (!group.Success)
            return false;

        var text = group.Value;
        var negative = text.StartsWith('-');
        var digits = negative ? text[1..] : text;

        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
            return false;

        weightKg = FixedPositionScaleProtocol.Scale(raw, negative, _decimalPlaces);
        return true;
    }
}
```

`SiagroB1.Commons/Scales/ScaleProtocolFactory.cs`:

```csharp
namespace SiagroB1.Commons.Scales;

public static class ScaleProtocolFactory
{
    public static IScaleProtocol Create(ScaleProtocolOptions options) =>
        string.Equals(options.Protocol, "Generic", StringComparison.OrdinalIgnoreCase)
            ? new RegexScaleProtocol(options)
            : new FixedPositionScaleProtocol(options);
}
```

- [x] **Step 5: Implementar o buffer de frames**

`SiagroB1.Commons/Scales/ScaleFrameBuffer.cs`:

```csharp
using System.Text;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Junta os pedaços que chegam do socket e devolve frames completos. O limite de tamanho protege
/// contra um terminador configurado errado, que faria o buffer crescer para sempre.
/// </summary>
public sealed class ScaleFrameBuffer(string terminator, int maxLength = 4096)
{
    private readonly StringBuilder _buffer = new();
    private readonly string _terminator = string.IsNullOrEmpty(terminator) ? "\n" : terminator;

    public IEnumerable<string> Append(string chunk)
    {
        _buffer.Append(chunk);

        var frames = new List<string>();
        var text = _buffer.ToString();

        int index;
        var consumed = 0;

        while ((index = text.IndexOf(_terminator, consumed, StringComparison.Ordinal)) >= 0)
        {
            frames.Add(text[consumed..index]);
            consumed = index + _terminator.Length;
        }

        _buffer.Clear();

        var tail = text[consumed..];

        if (tail.Length <= maxLength)
            _buffer.Append(tail);

        return frames;
    }
}
```

- [x] **Step 6: Rodar os testes e confirmar que passam**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~Scales`
Expected: PASS, 14 testes.

- [x] **Step 7: Stage**

```bash
git -C . add SiagroB1.Commons/Scales SiagroB1.Application.Tests/Scales
```

---

### Task 2: Estabilidade, leitura corrente e comprovantes

O coração da regra: o servidor decide o que é peso estável e emite o comprovante que o serviço de pesagem vai cobrar. Tudo puro e testável, sem depender de WebSocket.

**Files:**
- Create: `SiagroB1.Commons/Scales/StabilityDetector.cs`
- Create: `SiagroB1.Commons/Scales/LiveReadingStore.cs`
- Create: `SiagroB1.Commons/Scales/CaptureStore.cs`
- Test: `SiagroB1.Application.Tests/Scales/StabilityDetectorTests.cs`
- Test: `SiagroB1.Application.Tests/Scales/LiveReadingStoreTests.cs`
- Test: `SiagroB1.Application.Tests/Scales/CaptureStoreTests.cs`

**Interfaces:**
- Consumes: `ScaleReading`, `LiveWeight` (Task 1).
- Produces:
  - `class StabilityDetector { StabilityDetector(TimeSpan window, int minimumSamples); void Add(int weight, DateTime now); bool IsStable(DateTime now); int Current { get; } }`
  - `class LiveReadingStore { LiveReadingStore(TimeSpan window, int minimumSamples, TimeSpan offlineAfter); void Push(string scaleCode, int weight, DateTime now); void SetOffline(string scaleCode); LiveWeight Get(string scaleCode, DateTime now); }`
  - `record WeightCapture(Guid CaptureId, string ScaleCode, int Weight, string Username, DateTime ExpiresAt)`
  - `class CaptureStore { CaptureStore(TimeSpan ttl); WeightCapture Create(string scaleCode, int weight, string username, DateTime now); WeightCapture? Consume(Guid captureId, string username, DateTime now); }`

- [x] **Step 1: Escrever os testes (falhando)**

`SiagroB1.Application.Tests/Scales/StabilityDetectorTests.cs`:

```csharp
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class StabilityDetectorTests
{
    private static readonly DateTime T0 = new(2026, 8, 7, 10, 0, 0);

    private static StabilityDetector Detector() =>
        new(TimeSpan.FromSeconds(3), minimumSamples: 5);

    [Fact]
    public void Is_not_stable_before_the_minimum_number_of_samples()
    {
        var detector = Detector();

        for (var i = 0; i < 4; i++)
            detector.Add(20000, T0.AddMilliseconds(250 * i));

        Assert.False(detector.IsStable(T0.AddMilliseconds(1000)));
    }

    [Fact]
    public void Is_stable_after_enough_equal_samples_inside_the_window()
    {
        var detector = Detector();

        for (var i = 0; i < 6; i++)
            detector.Add(20000, T0.AddMilliseconds(250 * i));

        Assert.True(detector.IsStable(T0.AddMilliseconds(1250)));
        Assert.Equal(20000, detector.Current);
    }

    [Fact]
    public void A_different_reading_restarts_the_window()
    {
        var detector = Detector();

        for (var i = 0; i < 6; i++)
            detector.Add(20000, T0.AddMilliseconds(250 * i));

        detector.Add(20040, T0.AddMilliseconds(1500));

        Assert.False(detector.IsStable(T0.AddMilliseconds(1500)));
        Assert.Equal(20040, detector.Current);
    }

    [Fact]
    public void Samples_older_than_the_window_are_dropped()
    {
        var detector = Detector();

        detector.Add(19000, T0);

        for (var i = 1; i <= 6; i++)
            detector.Add(20000, T0.AddSeconds(4).AddMilliseconds(250 * i));

        Assert.True(detector.IsStable(T0.AddSeconds(6)));
    }
}
```

`SiagroB1.Application.Tests/Scales/LiveReadingStoreTests.cs`:

```csharp
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class LiveReadingStoreTests
{
    private static readonly DateTime T0 = new(2026, 8, 7, 10, 0, 0);

    private static LiveReadingStore Store() =>
        new(TimeSpan.FromSeconds(3), minimumSamples: 5, offlineAfter: TimeSpan.FromSeconds(2));

    [Fact]
    public void An_unknown_scale_is_offline()
    {
        var live = Store().Get("TS01", T0);

        Assert.False(live.Online);
        Assert.False(live.Stable);
        Assert.Equal(0, live.Weight);
    }

    [Fact]
    public void Reports_the_last_weight_while_readings_keep_arriving()
    {
        var store = Store();

        store.Push("TS01", 18000, T0);
        store.Push("TS01", 18500, T0.AddMilliseconds(250));

        var live = store.Get("TS01", T0.AddMilliseconds(300));

        Assert.True(live.Online);
        Assert.False(live.Stable);
        Assert.Equal(18500, live.Weight);
    }

    [Fact]
    public void Becomes_stable_after_enough_equal_readings()
    {
        var store = Store();

        for (var i = 0; i < 6; i++)
            store.Push("TS01", 32000, T0.AddMilliseconds(250 * i));

        var live = store.Get("TS01", T0.AddMilliseconds(1300));

        Assert.True(live.Stable);
        Assert.Equal(32000, live.Weight);
    }

    [Fact]
    public void Goes_offline_when_readings_stop_arriving()
    {
        var store = Store();

        for (var i = 0; i < 6; i++)
            store.Push("TS01", 32000, T0.AddMilliseconds(250 * i));

        var live = store.Get("TS01", T0.AddSeconds(10));

        Assert.False(live.Online);
        Assert.False(live.Stable);
    }

    [Fact]
    public void Set_offline_clears_the_reading_immediately()
    {
        var store = Store();

        store.Push("TS01", 32000, T0);
        store.SetOffline("TS01");

        Assert.False(store.Get("TS01", T0).Online);
    }

    [Fact]
    public void Scales_do_not_interfere_with_each_other()
    {
        var store = Store();

        store.Push("TS01", 10000, T0);
        store.Push("TS02", 20000, T0);

        Assert.Equal(10000, store.Get("TS01", T0).Weight);
        Assert.Equal(20000, store.Get("TS02", T0).Weight);
    }
}
```

`SiagroB1.Application.Tests/Scales/CaptureStoreTests.cs`:

```csharp
using SiagroB1.Commons.Scales;

namespace SiagroB1.Application.Tests.Scales;

public class CaptureStoreTests
{
    private static readonly DateTime T0 = new(2026, 8, 7, 10, 0, 0);

    private static CaptureStore Store() => new(TimeSpan.FromMinutes(10));

    [Fact]
    public void A_capture_can_be_consumed_once()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        var consumed = store.Consume(capture.CaptureId, "joao", T0.AddMinutes(1));

        Assert.NotNull(consumed);
        Assert.Equal(32000, consumed!.Weight);
        Assert.Equal("TS01", consumed.ScaleCode);
    }

    [Fact]
    public void A_capture_cannot_be_consumed_twice()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        store.Consume(capture.CaptureId, "joao", T0);

        Assert.Null(store.Consume(capture.CaptureId, "joao", T0));
    }

    [Fact]
    public void An_expired_capture_is_refused()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        Assert.Null(store.Consume(capture.CaptureId, "joao", T0.AddMinutes(11)));
    }

    [Fact]
    public void A_capture_of_another_user_is_refused()
    {
        var store = Store();
        var capture = store.Create("TS01", 32000, "joao", T0);

        Assert.Null(store.Consume(capture.CaptureId, "maria", T0));
    }

    [Fact]
    public void An_unknown_capture_is_refused()
    {
        Assert.Null(Store().Consume(Guid.NewGuid(), "joao", T0));
    }
}
```

- [x] **Step 2: Rodar e confirmar que falham**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~Scales`
Expected: falha de compilação — `StabilityDetector`, `LiveReadingStore` e `CaptureStore` não existem.

- [x] **Step 3: Implementar o `StabilityDetector`**

```csharp
namespace SiagroB1.Commons.Scales;

/// <summary>
/// Peso estável = todas as leituras da janela iguais entre si, com um mínimo de amostras. Uma
/// leitura diferente reinicia a janela: é o que impede gravar o peso de um caminhão em movimento.
/// </summary>
public sealed class StabilityDetector(TimeSpan window, int minimumSamples)
{
    private readonly List<ScaleReading> _readings = [];

    public int Current { get; private set; }

    public void Add(int weight, DateTime now)
    {
        if (_readings.Count > 0 && Current != weight)
            _readings.Clear();

        Current = weight;
        _readings.Add(new ScaleReading(weight, now));

        Trim(now);
    }

    public bool IsStable(DateTime now)
    {
        Trim(now);

        return _readings.Count >= minimumSamples;
    }

    private void Trim(DateTime now)
    {
        var cutoff = now - window;

        _readings.RemoveAll(x => x.Timestamp < cutoff);
    }
}
```

- [x] **Step 4: Implementar o `LiveReadingStore`**

```csharp
using System.Collections.Concurrent;

namespace SiagroB1.Commons.Scales;

/// <summary>
/// Leitura corrente de cada balança. Vive em memória, é escrita pela conexão WebSocket do Client e
/// lida pelo SSE e pela captura - por isso cada entrada tem seu próprio lock.
/// </summary>
public sealed class LiveReadingStore(TimeSpan window, int minimumSamples, TimeSpan offlineAfter)
{
    private sealed class Entry
    {
        public readonly StabilityDetector Detector = new(window, minimumSamples);
        public DateTime LastReadingAt;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Push(string scaleCode, int weight, DateTime now)
    {
        var entry = _entries.GetOrAdd(scaleCode, _ => new Entry());

        lock (entry)
        {
            entry.Detector.Add(weight, now);
            entry.LastReadingAt = now;
        }
    }

    /// <summary>Marca a balança como offline na hora - usado quando o TCP do indicador cai.</summary>
    public void SetOffline(string scaleCode) => _entries.TryRemove(scaleCode, out _);

    public LiveWeight Get(string scaleCode, DateTime now)
    {
        if (!_entries.TryGetValue(scaleCode, out var entry))
            return new LiveWeight(0, false, false);

        lock (entry)
        {
            var online = now - entry.LastReadingAt <= offlineAfter;

            return online
                ? new LiveWeight(entry.Detector.Current, entry.Detector.IsStable(now), true)
                : new LiveWeight(0, false, false);
        }
    }
}
```

- [x] **Step 5: Implementar o `CaptureStore`**

```csharp
using System.Collections.Concurrent;

namespace SiagroB1.Commons.Scales;

/// <summary>Comprovante de que um peso saiu da balança, e não do teclado.</summary>
public sealed record WeightCapture(
    Guid CaptureId,
    string ScaleCode,
    int Weight,
    string Username,
    DateTime ExpiresAt);

/// <summary>
/// Guarda os comprovantes emitidos. Uso único e com validade: um comprovante consumido não volta,
/// e um antigo não serve para gravar uma pesagem de hoje.
/// </summary>
public sealed class CaptureStore(TimeSpan ttl)
{
    private readonly ConcurrentDictionary<Guid, WeightCapture> _captures = new();

    public WeightCapture Create(string scaleCode, int weight, string username, DateTime now)
    {
        Purge(now);

        var capture = new WeightCapture(Guid.NewGuid(), scaleCode, weight, username, now + ttl);

        _captures[capture.CaptureId] = capture;

        return capture;
    }

    /// <summary>
    /// Devolve o comprovante e o remove. Nulo quando não existe, expirou ou é de outro usuário -
    /// quem chama trata os três casos com a mesma mensagem, para não virar oráculo de captura alheia.
    /// </summary>
    public WeightCapture? Consume(Guid captureId, string username, DateTime now)
    {
        if (!_captures.TryRemove(captureId, out var capture))
            return null;

        if (capture.ExpiresAt < now)
            return null;

        return string.Equals(capture.Username, username, StringComparison.OrdinalIgnoreCase)
            ? capture
            : null;
    }

    private void Purge(DateTime now)
    {
        foreach (var expired in _captures.Where(x => x.Value.ExpiresAt < now).Select(x => x.Key))
            _captures.TryRemove(expired, out _);
    }
}
```

- [x] **Step 6: Rodar os testes e confirmar que passam**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~Scales`
Expected: PASS, 29 testes (14 da Task 1 + 15 desta).

- [x] **Step 7: Stage**

```bash
git -C . add SiagroB1.Commons/Scales SiagroB1.Application.Tests/Scales
```

---

### Task 3: Modelo de dados e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/ScaleProtocolType.cs`
- Create: `SiagroB1.Domain/Enums/WeighingScalePurpose.cs`
- Create: `SiagroB1.Domain/Entities/UserTruckScale.cs`
- Modify: `SiagroB1.Domain/Entities/TruckScale.cs`
- Modify: `SiagroB1.Domain/Entities/Truck.cs`
- Modify: `SiagroB1.Domain/Entities/WeighingTicket.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`
- Test: `SiagroB1.Application.Tests/Infra/TruckScaleCaptureModelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `enum ScaleProtocolType { JundiaiBj850, Generic }`
  - `enum WeighingScalePurpose { Opening, Closing }`
  - `TruckScale`: `IpAddress`, `Port`, `Protocol`, `FramePrefixLength`, `WeightLength`, `DecimalPlaces`, `FrameTerminator`, `FramePattern`, `ValidateTare`, `TareToleranceKg`, `LogRawFrames`
  - `Truck.TareWeight` (`int?`)
  - `WeighingTicket`: `FirstWeighScaleCode`, `SecondWeighScaleCode` (`string?`), `FirstWeighCaptured`, `SecondWeighCaptured` (`bool`)
  - `UserTruckScale { Guid Id; string Username; string TruckScaleCode; TruckScale? TruckScale; WeighingScalePurpose Purpose }`
  - `AppDbContext.UserTruckScales`

- [x] **Step 1: Escrever o teste de modelo (falhando)**

O projeto já testa o modelo relacional assim (veja `SiagroB1.Application.Tests/Infra/AppDbContextModelTests.cs`). Crie `SiagroB1.Application.Tests/Infra/TruckScaleCaptureModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class TruckScaleCaptureModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public void UserTruckScales_maps_to_its_table_with_a_unique_index_per_purpose()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(UserTruckScale))!;

        Assert.Equal("USER_TRUCK_SCALES", entity.GetTableName());

        var unique = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.Equal(
            ["Username", "Purpose"],
            unique.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void UserTruckScales_has_no_navigation_to_users()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(UserTruckScale))!;

        // USERS vive no banco COMMON: uma FK aqui seria entre bancos diferentes.
        Assert.DoesNotContain(entity.GetNavigations(), n => n.Name == "User");
        Assert.Contains(entity.GetNavigations(), n => n.Name == "TruckScale");
    }

    [Fact]
    public void TruckScale_carries_the_connection_and_tare_configuration()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(TruckScale))!;

        foreach (var property in new[]
                 {
                     "IpAddress", "Port", "Protocol", "ValidateTare", "TareToleranceKg", "LogRawFrames"
                 })
        {
            Assert.NotNull(entity.FindProperty(property));
        }
    }

    [Fact]
    public void Truck_tare_is_optional()
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(Truck))!.FindProperty("TareWeight")!;

        // Nulo de propósito: obrigatório travaria a gravação dos caminhões legados sem tara.
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void WeighingTicket_records_the_origin_of_each_weight()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(WeighingTicket))!;

        Assert.True(entity.FindProperty("FirstWeighScaleCode")!.IsNullable);
        Assert.True(entity.FindProperty("SecondWeighScaleCode")!.IsNullable);
        Assert.False(entity.FindProperty("FirstWeighCaptured")!.IsNullable);
        Assert.False(entity.FindProperty("SecondWeighCaptured")!.IsNullable);
    }

    [Fact]
    public void Purpose_has_exactly_two_values()
    {
        Assert.Equal(
            [WeighingScalePurpose.Opening, WeighingScalePurpose.Closing],
            Enum.GetValues<WeighingScalePurpose>());
    }
}
```

- [x] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~TruckScaleCaptureModelTests`
Expected: falha de compilação — `UserTruckScale` e `WeighingScalePurpose` não existem.

- [x] **Step 3: Criar os enums**

`SiagroB1.Domain/Enums/ScaleProtocolType.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum ScaleProtocolType
{
    JundiaiBj850,  // Posição fixa - preset do indicador Jundiaí BJ850
    Generic        // Expressão regular configurável
}
```

`SiagroB1.Domain/Enums/WeighingScalePurpose.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum WeighingScalePurpose
{
    Opening,  // Abertura - primeira pesagem
    Closing   // Encerramento - segunda pesagem
}
```

- [x] **Step 4: Estender `TruckScale`**

Substitua o corpo de `SiagroB1.Domain/Entities/TruckScale.cs` por:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_SCALES")]
[Index(nameof(Code), IsUnique = true)]
public class TruckScale
{
    [Key]
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Localization { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? IpAddress { get; set; }

    public int Port { get; set; }

    public ScaleProtocolType Protocol { get; set; } = ScaleProtocolType.JundiaiBj850;

    // Sobrescritas do preset. Nulas usam o padrão do protocolo - é assim que o BJ850 real é
    // calibrado em campo, sem recompilar.
    public int? FramePrefixLength { get; set; }

    public int? WeightLength { get; set; }

    public int? DecimalPlaces { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? FrameTerminator { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? FramePattern { get; set; }

    public bool ValidateTare { get; set; }

    public int TareToleranceKg { get; set; }

    /// <summary>Grava os frames crus no log, para calibrar o protocolo em campo.</summary>
    public bool LogRawFrames { get; set; }
}
```

- [x] **Step 5: Estender `Truck` e `WeighingTicket`**

Em `SiagroB1.Domain/Entities/Truck.cs`, adicione dentro da classe:

```csharp
    /// <summary>
    /// Tara do veículo em quilos. Nula de propósito: torná-la obrigatória travaria a gravação dos
    /// caminhões já cadastrados sem tara. Quem a cobra é a validação da pesagem, e só quando a
    /// balança tem <see cref="TruckScale.ValidateTare"/> ligado.
    /// </summary>
    public int? TareWeight { get; set; }
```

Em `SiagroB1.Domain/Entities/WeighingTicket.cs`, logo depois de `FirstWeighUsername`:

```csharp
    /// <summary>Balança que produziu a leitura. Coluna simples, sem FK, como FreightUmCode.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string? FirstWeighScaleCode { get; set; }

    /// <summary>Falso quando o peso foi digitado por quem tem permissão para isso.</summary>
    public bool FirstWeighCaptured { get; set; }
```

e depois de `SecondWeighUsername`:

```csharp
    [Column(TypeName = "VARCHAR(11)")]
    public string? SecondWeighScaleCode { get; set; }

    public bool SecondWeighCaptured { get; set; }
```

- [x] **Step 6: Criar `UserTruckScale` e registrar o `DbSet`**

`SiagroB1.Domain/Entities/UserTruckScale.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Balança que um usuário opera em cada etapa da pesagem. Onde há uma balança só, a mesma é
/// informada nas duas finalidades.
///
/// Sem FK para USERS de propósito: aquela tabela vive no banco COMMON e esta no banco da empresa.
/// A chave é o Username, que é o que a API tem em mãos (User.Identity.Name) e o mesmo padrão de
/// WEIGHING_TICKETS.FirstWeighUsername.
/// </summary>
[Table("USER_TRUCK_SCALES")]
[Index(nameof(Username), nameof(Purpose), IsUnique = true)]
public class UserTruckScale
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    [MaxLength(50)]
    public required string Username { get; set; }

    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    [ForeignKey(nameof(TruckScale))]
    public required string TruckScaleCode { get; set; }

    public virtual TruckScale? TruckScale { get; set; }

    public required WeighingScalePurpose Purpose { get; set; }
}
```

Em `SiagroB1.Infra/Context/AppDbContext.cs`, junto do `DbSet<TruckScale> TruckScales`:

```csharp
    public DbSet<UserTruckScale> UserTruckScales { get; set; }
```

- [x] **Step 7: Rodar os testes de modelo**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~TruckScaleCaptureModelTests`
Expected: PASS, 6 testes.

- [x] **Step 8: Gerar a migration**

```bash
dotnet ef migrations add AddTruckScaleCaptureConfiguration \
  --project SiagroB1.Migrations \
  --startup-project SiagroB1.Web \
  --context AppDbContext
```

Abra a migration gerada em `SiagroB1.Migrations/AppContext/` e confira, **antes de aplicar**:
- `TRUCK_SCALES` recebe as colunas novas com `defaultValue` que não quebra as linhas existentes (`Port` = 0, `Protocol` = 0, `ValidateTare` = false, `TareToleranceKg` = 0, `LogRawFrames` = false).
- `WEIGHING_TICKETS.FirstWeighCaptured` e `SecondWeighCaptured` são `bit NOT NULL DEFAULT 0`.
- `TRUCKS.TareWeight` é `int NULL`, **sem** default.
- `USER_TRUCK_SCALES` é criada com FK para `TRUCK_SCALES` e índice único `(Username, Purpose)`.

- [x] **Step 9: Aplicar a migration no ambiente de desenvolvimento**

```bash
ASPNETCORE_ENVIRONMENT=yktb dotnet ef database update \
  --project SiagroB1.Migrations \
  --startup-project SiagroB1.Web \
  --context AppDbContext
```

Expected: `Done.` sem erro. Confirme na saída que a connection string é a do banco de desenvolvimento antes de deixar rodar.

- [x] **Step 10: Stage**

```bash
git -C . add SiagroB1.Domain SiagroB1.Infra SiagroB1.Migrations SiagroB1.Application.Tests/Infra
```

---

### Task 4: Permissões efetivas do usuário

Liga o fio que nunca existiu: as tabelas de permissão existem, mas nada as consome.

**Files:**
- Create: `SiagroB1.Application/Interfaces/IUserPermissions.cs`
- Create: `SiagroB1.Application/Services/Security/UserPermissionsService.cs`
- Create: migration em `SiagroB1.Migrations/CommonContext/`
- Modify: `SiagroB1.Security/Dtos/UserInfo.cs`
- Modify: `SiagroB1.Security/Services/AuthService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Security/UserPermissionsServiceTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `const string PermissionCodes.WeighingManualEntry = "WEIGHING_MANUAL_ENTRY"`
  - `interface IUserPermissions { Task<bool> HasAsync(string username, string permissionCode); Task<List<string>> GetAsync(string username); }`
  - `UserInfo.Permissions` (`List<string>`)

- [x] **Step 1: Escrever os testes (falhando)**

`SiagroB1.Application.Tests/Security/UserPermissionsServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Security;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Security;

public class UserPermissionsServiceTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>usuário -> perfil -> papel -> permissão, que é o caminho real do cadastro.</summary>
    private static void GrantPermission(CommonDbContext db, string username, string permissionCode,
        bool isAdmin = false)
    {
        var user = new User { Username = username, FullName = username, IsAdmin = isAdmin };
        db.Users.Add(user);

        db.Permissions.Add(new Permission { Code = permissionCode, Description = permissionCode });
        db.Roles.Add(new Role { Code = "OPERADOR" });
        db.Profiles.Add(new Profile { Code = "BALANCA", Description = "Balança" });
        db.RolesPermissions.Add(new RolePermission { RoleCode = "OPERADOR", PermissionCode = permissionCode });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "BALANCA", RoleCode = "OPERADOR" });
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, ProfileCode = "BALANCA" });

        db.SaveChanges();
    }

    [Fact]
    public async Task Returns_the_permission_granted_through_profile_and_role()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        var service = new UserPermissionsService(db);

        Assert.True(await service.HasAsync("joao", "WEIGHING_MANUAL_ENTRY"));
        Assert.Equal(["WEIGHING_MANUAL_ENTRY"], await service.GetAsync("joao"));
    }

    [Fact]
    public async Task Returns_false_for_a_permission_that_was_not_granted()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "SOME_OTHER_PERMISSION");

        var service = new UserPermissionsService(db);

        Assert.False(await service.HasAsync("joao", "WEIGHING_MANUAL_ENTRY"));
    }

    [Fact]
    public async Task An_admin_has_every_permission()
    {
        using var db = CreateDb();
        GrantPermission(db, "admin", "SOME_OTHER_PERMISSION", isAdmin: true);

        var service = new UserPermissionsService(db);

        Assert.True(await service.HasAsync("admin", "WEIGHING_MANUAL_ENTRY"));
    }

    [Fact]
    public async Task An_unknown_user_has_no_permission()
    {
        using var db = CreateDb();

        var service = new UserPermissionsService(db);

        Assert.False(await service.HasAsync("ninguem", "WEIGHING_MANUAL_ENTRY"));
        Assert.Empty(await service.GetAsync("ninguem"));
    }

    [Fact]
    public async Task Duplicated_grants_are_returned_once()
    {
        using var db = CreateDb();
        GrantPermission(db, "joao", "WEIGHING_MANUAL_ENTRY");

        db.Roles.Add(new Role { Code = "SUPERVISOR" });
        db.RolesPermissions.Add(new RolePermission
        {
            RoleCode = "SUPERVISOR",
            PermissionCode = "WEIGHING_MANUAL_ENTRY"
        });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "BALANCA", RoleCode = "SUPERVISOR" });
        await db.SaveChangesAsync();

        var service = new UserPermissionsService(db);

        Assert.Single(await service.GetAsync("joao"));
    }
}
```

Antes de rodar, confirme os nomes das propriedades de `Profile`, `ProfileRole` e `UserProfile` abrindo `SiagroB1.Domain/Entities/Common/`; ajuste o helper se algum diferir.

- [x] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~UserPermissionsServiceTests`
Expected: falha de compilação — `UserPermissionsService` não existe.

- [x] **Step 3: Implementar a interface e o serviço**

`SiagroB1.Application/Interfaces/IUserPermissions.cs`:

```csharp
namespace SiagroB1.Application.Interfaces;

/// <summary>Códigos de permissão consumidos pelo sistema. O cadastro é livre; estes têm efeito.</summary>
public static class PermissionCodes
{
    /// <summary>Digitar o peso manualmente na pesagem, em vez de capturá-lo da balança.</summary>
    public const string WeighingManualEntry = "WEIGHING_MANUAL_ENTRY";
}

public interface IUserPermissions
{
    Task<bool> HasAsync(string username, string permissionCode);

    Task<List<string>> GetAsync(string username);
}
```

`SiagroB1.Application/Services/Security/UserPermissionsService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Security;

/// <summary>
/// Permissões efetivas de um usuário: usuário -> perfis -> papéis -> permissões. Administrador
/// passa por cima de tudo, como já acontece no resto do sistema.
/// </summary>
public class UserPermissionsService(CommonDbContext db) : IUserPermissions
{
    public async Task<bool> HasAsync(string username, string permissionCode)
    {
        if (await IsAdminAsync(username))
            return true;

        var permissions = await GetAsync(username);

        return permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<List<string>> GetAsync(string username)
    {
        var query =
            from u in db.Users
            join up in db.UserProfiles on u.Id equals up.UserId
            join pr in db.ProfileRoles on up.ProfileCode equals pr.ProfileCode
            join rp in db.RolesPermissions on pr.RoleCode equals rp.RoleCode
            where u.Username == username && u.IsActive
            select rp.PermissionCode;

        return await query.Distinct().ToListAsync();
    }

    private async Task<bool> IsAdminAsync(string username) =>
        await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Username == username && u.IsActive && u.IsAdmin);
}
```

Registre em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, junto dos demais `AddScoped`:

```csharp
        services.AddScoped<IUserPermissions, UserPermissionsService>();
```

- [x] **Step 4: Rodar os testes e confirmar que passam**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~UserPermissionsServiceTests`
Expected: PASS, 5 testes.

- [x] **Step 5: Publicar as permissões no `/status` e no login**

Em `SiagroB1.Security/Dtos/UserInfo.cs`, adicione:

```csharp
    /// <summary>
    /// Permissões efetivas do usuário. A tela usa isto para não oferecer uma ação que voltaria
    /// recusada; quem decide de fato continua sendo o servidor.
    /// </summary>
    public List<string> Permissions { get; set; } = [];
```

Em `SiagroB1.Security/Services/AuthService.cs`, adicione o método privado e preencha nos dois pontos que montam `UserInfo` (`GetUserInfoAsync` e `ToUserInfo`; como `ToUserInfo` é `static`, transforme-o em método de instância `async` ou preencha `Permissions` no chamador, logo depois de montar o DTO):

```csharp
        /// <summary>Permissões efetivas: usuário -> perfis -> papéis -> permissões.</summary>
        private async Task<List<string>> GetPermissionsAsync(Guid userId)
        {
            var query =
                from up in db.UserProfiles
                join pr in db.ProfileRoles on up.ProfileCode equals pr.ProfileCode
                join rp in db.RolesPermissions on pr.RoleCode equals rp.RoleCode
                where up.UserId == userId
                select rp.PermissionCode;

            return await query.Distinct().ToListAsync();
        }
```

- [x] **Step 6: Seed da permissão no banco COMMON**

```bash
dotnet ef migrations add SeedWeighingManualEntryPermission \
  --project SiagroB1.Migrations \
  --startup-project SiagroB1.Web \
  --context CommonDbContext
```

No `Up()` da migration gerada (idempotente, porque o código pode já ter sido cadastrado à mão):

```csharp
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'WEIGHING_MANUAL_ENTRY')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('WEIGHING_MANUAL_ENTRY', 'Digitar o peso manualmente na pesagem');
                """);
```

No `Down()`:

```csharp
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'WEIGHING_MANUAL_ENTRY';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code = 'WEIGHING_MANUAL_ENTRY';");
```

Aplique:

```bash
ASPNETCORE_ENVIRONMENT=yktb dotnet ef database update \
  --project SiagroB1.Migrations \
  --startup-project SiagroB1.Web \
  --context CommonDbContext
```

- [x] **Step 7: Compilar e stage**

Run: `dotnet build SiagroB1.sln`
Expected: build sem erro.

```bash
git -C . add SiagroB1.Application/Interfaces SiagroB1.Application/Services/Security \
  SiagroB1.Application.Tests/Security SiagroB1.Migrations/CommonContext
```

---

### Task 5: Regra de pesagem — permissão, comprovante e tara

O núcleo da funcionalidade no servidor. Sem isto, a captura é decoração.

**Files:**
- Modify: `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsFirstWeighingService.cs`
- Modify: `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsSecondWeighingService.cs`
- Create: `SiagroB1.Application/Services/WeighingTickets/WeighingCaptureValidator.cs`
- Modify: `SiagroB1.Web/Actions/WeighingTickets/WeighingTicketsFirstWeighingController.cs`
- Modify: `SiagroB1.Web/Actions/WeighingTickets/WeighingTicketsSecondWeighingController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/WeighingTickets/WeighingCaptureRulesTests.cs`

**Interfaces:**
- Consumes: `CaptureStore`, `WeightCapture` (Task 2); `IUserPermissions`, `PermissionCodes` (Task 4); `UserTruckScale`, `WeighingScalePurpose`, `TruckScale.ValidateTare/TareToleranceKg`, `Truck.TareWeight` (Task 3).
- Produces:
  - `class WeighingCaptureValidator { WeighingCaptureValidator(IUnitOfWork db, IUserPermissions permissions, CaptureStore captures); Task<WeighingWeightOrigin> ResolveAsync(string username, int weigh, Guid? captureId, WeighingScalePurpose purpose, string truckCode); }`
  - `record WeighingWeightOrigin(string? ScaleCode, bool Captured)`
  - `WeighingTicketsFirstWeighingService.ExecuteAsync(Guid key, int weigh, string? comments, string username, Guid? captureId)`
  - `WeighingTicketsSecondWeighingService.ExecuteAsync(...)` com a mesma assinatura.

- [x] **Step 1: Escrever os testes das regras (falhando)**

`SiagroB1.Application.Tests/WeighingTickets/WeighingCaptureRulesTests.cs`:

```csharp
using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WeighingTickets;

public class WeighingCaptureRulesTests
{
    private sealed class FakePermissions(bool canType) : IUserPermissions
    {
        public Task<bool> HasAsync(string username, string permissionCode) => Task.FromResult(canType);

        public Task<List<string>> GetAsync(string username)
        {
            var granted = canType
                ? new List<string> { PermissionCodes.WeighingManualEntry }
                : new List<string>();

            return Task.FromResult(granted);
        }
    }

    private static readonly DateTime Now = DateTime.Now;

    private static async Task<(IUnitOfWork db, WeighingTicket ticket)> SeedAsync(
        int? tareWeight = 15000,
        bool validateTare = true,
        int tolerance = 200)
    {
        var db = TestDb.CreateUnitOfWork();

        db.Context.TruckScales.Add(new TruckScale
        {
            Code = "TS01",
            Name = "Balança 1",
            Localization = "Portaria",
            IpAddress = "192.168.1.201",
            Port = 4000,
            ValidateTare = validateTare,
            TareToleranceKg = tolerance
        });

        db.Context.UserTruckScales.Add(new UserTruckScale
        {
            Username = "joao",
            TruckScaleCode = "TS01",
            Purpose = WeighingScalePurpose.Opening
        });

        db.Context.Trucks.Add(new Truck { Code = "ABC1D23", TareWeight = tareWeight });

        var ticket = new WeighingTicket
        {
            Key = Guid.NewGuid(),
            Type = WeighingTicketType.Receipt,
            ItemCode = "SOJA",
            CardCode = "F0001",
            TruckCode = "ABC1D23",
            TruckDriverCode = "1",
            Stage = WeighingTicketStage.ReadyForFirstWeighing
        };

        db.Context.WeighingTickets.Add(ticket);

        await db.SaveChangesAsync();

        return (db, ticket);
    }

    private static WeighingTicketsFirstWeighingService Service(
        IUnitOfWork db, CaptureStore captures, bool canType) =>
        new(db, new WeighingCaptureValidator(db, new FakePermissions(canType), captures));

    [Fact]
    public async Task Without_the_permission_a_capture_is_required()
    {
        var (db, ticket) = await SeedAsync();
        var service = Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: false);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null));

        Assert.Contains("capturado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_a_valid_capture_the_weight_is_saved_and_marked_as_captured()
    {
        var (db, ticket) = await SeedAsync();
        var captures = new CaptureStore(TimeSpan.FromMinutes(10));
        var capture = captures.Create("TS01", 32000, "joao", Now);

        await Service(db, captures, canType: false)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", capture.CaptureId);

        var saved = db.Context.WeighingTickets.Single();

        Assert.Equal(32000, saved.FirstWeighValue);
        Assert.Equal("TS01", saved.FirstWeighScaleCode);
        Assert.True(saved.FirstWeighCaptured);
        Assert.Equal(WeighingTicketStage.ReadyForSecondWeighing, saved.Stage);
    }

    [Fact]
    public async Task A_capture_cannot_be_reused()
    {
        var (db, ticket) = await SeedAsync();
        var captures = new CaptureStore(TimeSpan.FromMinutes(10));
        var capture = captures.Create("TS01", 32000, "joao", Now);

        await Service(db, captures, canType: false)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", capture.CaptureId);

        ticket.Stage = WeighingTicketStage.ReadyForFirstWeighing;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, captures, canType: false)
                .ExecuteAsync(ticket.Key, 32000, null, "joao", capture.CaptureId));
    }

    [Fact]
    public async Task A_weight_that_does_not_match_the_capture_is_refused()
    {
        var (db, ticket) = await SeedAsync();
        var captures = new CaptureStore(TimeSpan.FromMinutes(10));
        var capture = captures.Create("TS01", 32000, "joao", Now);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, captures, canType: false)
                .ExecuteAsync(ticket.Key, 31000, null, "joao", capture.CaptureId));
    }

    [Fact]
    public async Task With_the_permission_a_typed_weight_is_accepted_and_not_marked_as_captured()
    {
        var (db, ticket) = await SeedAsync();

        await Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null);

        var saved = db.Context.WeighingTickets.Single();

        Assert.Equal(32000, saved.FirstWeighValue);
        Assert.False(saved.FirstWeighCaptured);
    }

    [Fact]
    public async Task A_weight_below_the_tare_minus_the_tolerance_is_refused()
    {
        var (db, ticket) = await SeedAsync(tareWeight: 15000, tolerance: 200);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
                .ExecuteAsync(ticket.Key, 14700, null, "joao", captureId: null));

        Assert.Contains("tara", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_weight_inside_the_tolerance_is_accepted()
    {
        var (db, ticket) = await SeedAsync(tareWeight: 15000, tolerance: 200);

        await Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
            .ExecuteAsync(ticket.Key, 14900, null, "joao", captureId: null);

        Assert.Equal(14900, db.Context.WeighingTickets.Single().FirstWeighValue);
    }

    [Fact]
    public async Task A_truck_without_a_registered_tare_is_refused_when_validation_is_on()
    {
        var (db, ticket) = await SeedAsync(tareWeight: null);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
                .ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null));

        Assert.Contains("tara", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tare_is_not_validated_when_the_scale_has_it_turned_off()
    {
        var (db, ticket) = await SeedAsync(tareWeight: null, validateTare: false);

        await Service(db, new CaptureStore(TimeSpan.FromMinutes(10)), canType: true)
            .ExecuteAsync(ticket.Key, 32000, null, "joao", captureId: null);

        Assert.Equal(32000, db.Context.WeighingTickets.Single().FirstWeighValue);
    }
}
```

- [x] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~WeighingCaptureRulesTests`
Expected: falha de compilação — `WeighingCaptureValidator` não existe e o serviço tem outra assinatura.

- [x] **Step 3: Implementar o validador**

`SiagroB1.Application/Services/WeighingTickets/WeighingCaptureValidator.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

/// <summary>De onde veio o peso: qual balança e se foi capturado ou digitado.</summary>
public sealed record WeighingWeightOrigin(string? ScaleCode, bool Captured);

/// <summary>
/// Regras comuns às duas pesagens: quem pode digitar, o comprovante de captura e a tara.
///
/// O comprovante é o que impede burlar a restrição por fora da tela: sem a permissão, o peso
/// precisa ter nascido no servidor, e o comprovante é de uso único.
/// </summary>
public class WeighingCaptureValidator(
    IUnitOfWork db,
    IUserPermissions permissions,
    CaptureStore captures)
{
    public async Task<WeighingWeightOrigin> ResolveAsync(
        string username,
        int weigh,
        Guid? captureId,
        WeighingScalePurpose purpose,
        string truckCode)
    {
        var canType = await permissions.HasAsync(username, PermissionCodes.WeighingManualEntry);

        string? scaleCode = null;
        var captured = false;

        if (captureId.HasValue)
        {
            var capture = captures.Consume(captureId.Value, username, DateTime.Now)
                ?? throw new ApplicationException(
                    "A captura do peso expirou ou já foi utilizada. Capture o peso novamente.");

            if (capture.Weight != weigh)
                throw new ApplicationException(
                    "O peso informado não confere com o peso capturado na balança.");

            scaleCode = capture.ScaleCode;
            captured = true;
        }
        else if (!canType)
        {
            throw new ApplicationException(
                "O peso deve ser capturado da balança. Este usuário não pode digitar o peso.");
        }

        scaleCode ??= await GetConfiguredScaleCodeAsync(username, purpose);

        await ValidateTareAsync(scaleCode, truckCode, weigh);

        return new WeighingWeightOrigin(scaleCode, captured);
    }

    private async Task<string?> GetConfiguredScaleCodeAsync(string username, WeighingScalePurpose purpose) =>
        await db.Context.UserTruckScales
            .AsNoTracking()
            .Where(x => x.Username == username && x.Purpose == purpose)
            .Select(x => x.TruckScaleCode)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Vale para as duas pesagens: nenhum peso lido pode ser menor que a tara cadastrada menos a
    /// tolerância da balança. Sem balança determinável, a validação não incide.
    /// </summary>
    private async Task ValidateTareAsync(string? scaleCode, string truckCode, int weigh)
    {
        if (scaleCode == null)
            return;

        var scale = await db.Context.TruckScales
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == scaleCode);

        if (scale is not { ValidateTare: true })
            return;

        var tare = await db.Context.Trucks
            .AsNoTracking()
            .Where(x => x.Code == truckCode)
            .Select(x => x.TareWeight)
            .FirstOrDefaultAsync();

        if (tare == null)
            throw new ApplicationException(
                "Caminhão sem tara cadastrada. Informe a tara no cadastro do veículo antes de pesar.");

        var minimum = tare.Value - scale.TareToleranceKg;

        if (weigh < minimum)
            throw new ApplicationException(
                $"Peso de {weigh:N0} kg é menor que a tara cadastrada de {tare.Value:N0} kg " +
                $"menos a tolerância de {scale.TareToleranceKg:N0} kg.");
    }
}
```

- [x] **Step 4: Ligar o validador nos dois serviços de pesagem**

`WeighingTicketsFirstWeighingService.cs` passa a ser:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsFirstWeighingService(IUnitOfWork db, WeighingCaptureValidator validator)
{
    public async Task ExecuteAsync(Guid key, int weigh, string? comments, string username, Guid? captureId)
    {
        if (weigh <= 0)
            throw new ApplicationException("Quantidade deve ser maior que zero.");

        var ticket = await db.Context.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForFirstWeighing)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        var origin = await validator.ResolveAsync(
            username, weigh, captureId, WeighingScalePurpose.Opening, ticket.TruckCode);

        ticket.Status = WeighingTicketStatus.Processing;
        ticket.FirstWeighValue = weigh;
        ticket.FirstWeighDateTime = DateTime.Now;
        ticket.Stage = WeighingTicketStage.ReadyForSecondWeighing;
        ticket.Comments = comments;
        ticket.FirstWeighUsername = username;
        ticket.FirstWeighScaleCode = origin.ScaleCode;
        ticket.FirstWeighCaptured = origin.Captured;

        await db.SaveChangesAsync();
    }
}
```

Nota: o `try/catch` que reembrulhava toda exceção em `ApplicationException` sai — ele engolia a mensagem de negócio do validador e transformava tudo em texto de infraestrutura.

`WeighingTicketsSecondWeighingService.cs` recebe a mesma estrutura, trocando: `ReadyForSecondWeighing` como estágio de entrada, `ReadyForCompleting` como estágio de saída, `SecondWeigh*` nas propriedades e `WeighingScalePurpose.Closing` na chamada do validador. Abra o arquivo atual e preserve os demais efeitos que ele já tenha.

- [x] **Step 5: Registrar as dependências**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<WeighingCaptureValidator>();
        services.AddSingleton(new CaptureStore(TimeSpan.FromMinutes(10)));
```

- [x] **Step 6: Rodar os testes e confirmar que passam**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~WeighingCaptureRulesTests`
Expected: PASS, 9 testes.

- [x] **Step 7: Expor `CaptureId` nas ações OData**

Em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, nas duas ações:

```csharp
        weighingTicketsFirstWeighing.Parameter<string>("CaptureId");
```

```csharp
        weighingTicketsSecondWeighing.Parameter<string>("CaptureId");
```

Nos dois controllers de ação, troque a leitura dos parâmetros. **Atenção:** `TryGetValue` devolve `true` com valor `null` para parâmetro string — chamar `.ToString()` direto estoura.

```csharp
            var comments = parameters.TryGetValue("Comments", out var commentsObj)
                ? commentsObj?.ToString()
                : null;

            Guid? captureId = null;
            if (parameters.TryGetValue("CaptureId", out var captureObj)
                && Guid.TryParse(captureObj?.ToString(), out var parsed))
            {
                captureId = parsed;
            }

            await service.ExecuteAsync(key, value, comments, userName, captureId);
```

Remova a exigência de `Comments` do `if (!parameters.TryGetValue(...))` — ele passa a ser opcional de fato.

- [x] **Step 8: Compilar, rodar a suíte inteira e stage**

Run: `dotnet build SiagroB1.sln`
Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`
Expected: build sem erro; a suíte inteira passa (nenhum teste pré-existente quebrado).

```bash
git -C . add SiagroB1.Application/Services/WeighingTickets SiagroB1.Web/Actions/WeighingTickets \
  SiagroB1.Web/ODataConfig SiagroB1.Web/Extensions SiagroB1.Application.Tests/WeighingTickets
```

---

### Task 6: Hub WebSocket e endpoints de peso ao vivo

**Files:**
- Create: `SiagroB1.Web/Sockets/TruckScale/TruckScaleHub.cs`
- Create: `SiagroB1.Web/Sockets/TruckScale/ScaleConfigProvider.cs`
- Create: `SiagroB1.Web/Controllers/ScalesController.cs`
- Modify: `SiagroB1.Web/Sockets/TruckScale/TruckScaleWebSocketEndpoint.cs`
- Modify: `SiagroB1.Web/Dtos` (mensagens WS) — criar `SiagroB1.Web/Sockets/TruckScale/ScaleMessages.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Delete: `SiagroB1.Web/Sockets/TruckScale/TruckScaleCaptureController.cs`, `TruckScaleWebSocketConnectionManager.cs`, `SiagroB1.Web/Sockets/PendingRequestStore.cs`, `SiagroB1.Web/Sockets/WsMessageHandler.cs`

**Interfaces:**
- Consumes: `LiveReadingStore`, `CaptureStore`, `LiveWeight` (Task 2); `TruckScale` (Task 3).
- Produces:
  - Mensagens WS Web→Client: `{"action":"scale_config","data":{ip,port,protocol,framePrefixLength,weightLength,decimalPlaces,frameTerminator,framePattern,logRawFrames}}`
  - Mensagens WS Client→Web: `{"action":"weight_tick","data":{"weight":32000}}` e `{"action":"scale_status","data":{"online":false}}`
  - `GET /scales/{code}/live` → SSE de `{"weight":32000,"stable":true,"online":true}`
  - `POST /scales/{code}/capture` → `200 {"captureId":"...","weight":32000}`, `409` offline, `408` não estabilizou

- [x] **Step 1: Remover o desenho antigo**

```bash
git -C . rm SiagroB1.Web/Sockets/TruckScale/TruckScaleCaptureController.cs \
            SiagroB1.Web/Sockets/TruckScale/TruckScaleWebSocketConnectionManager.cs \
            SiagroB1.Web/Sockets/PendingRequestStore.cs \
            SiagroB1.Web/Sockets/WsMessageHandler.cs
```

Remova também de `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` as linhas
`services.AddSingleton<TruckScaleWebSocketConnectionManager>();`,
`services.AddSingleton<WsMessageHandler>();` e
`services.AddSingleton<PendingRequestStore>();`.

- [x] **Step 2: Criar os contratos de mensagem**

`SiagroB1.Web/Sockets/TruckScale/ScaleMessages.cs`:

```csharp
namespace SiagroB1.Web.Sockets.TruckScale;

/// <summary>Envelope das mensagens trocadas com o SiagroB1.Client.</summary>
public sealed class ScaleMessage
{
    public string? Action { get; set; }

    public ScaleMessageData? Data { get; set; }
}

public sealed class ScaleMessageData
{
    public int? Weight { get; set; }

    public bool? Online { get; set; }

    public string? RawFrame { get; set; }
}

/// <summary>Configuração enviada ao Client assim que ele conecta.</summary>
public sealed class ScaleConfigPayload
{
    public string? Ip { get; set; }

    public int Port { get; set; }

    public string Protocol { get; set; } = "JundiaiBj850";

    public int FramePrefixLength { get; set; } = 1;

    public int WeightLength { get; set; } = 6;

    public int DecimalPlaces { get; set; }

    public string FrameTerminator { get; set; } = "\n";

    public string? FramePattern { get; set; }

    public bool LogRawFrames { get; set; }
}
```

- [x] **Step 3: Criar o hub e o provedor de configuração**

`SiagroB1.Web/Sockets/TruckScale/TruckScaleHub.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.WebSockets;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Web.Sockets.TruckScale;

/// <summary>
/// Conexões vivas do SiagroB1.Client e leitura corrente de cada balança.
///
/// ConcurrentDictionary, e não Dictionary: com duas balanças, duas conexões escrevem aqui ao
/// mesmo tempo enquanto o SSE lê - o dicionário comum corrompia silenciosamente.
/// </summary>
public class TruckScaleHub(LiveReadingStore readings)
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string scaleCode, WebSocket socket) => _connections[scaleCode] = socket;

    public void Remove(string scaleCode)
    {
        _connections.TryRemove(scaleCode, out _);
        readings.SetOffline(scaleCode);
    }

    public bool IsConnected(string scaleCode) => _connections.ContainsKey(scaleCode);

    public void PushWeight(string scaleCode, int weight) =>
        readings.Push(scaleCode, weight, DateTime.Now);

    public void SetOffline(string scaleCode) => readings.SetOffline(scaleCode);

    public LiveWeight GetLive(string scaleCode) => readings.Get(scaleCode, DateTime.Now);
}
```

`SiagroB1.Web/Sockets/TruckScale/ScaleConfigProvider.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Web.Sockets.TruckScale;

/// <summary>
/// Monta a configuração que o Client recebe ao conectar. O cadastro é a fonte única: trocar o IP
/// de uma balança não exige acesso à máquina onde o Client roda.
/// </summary>
public class ScaleConfigProvider(AppDbContext db)
{
    public async Task<ScaleConfigPayload?> GetAsync(string scaleCode)
    {
        var scale = await db.TruckScales
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == scaleCode);

        if (scale == null)
            return null;

        return new ScaleConfigPayload
        {
            Ip = scale.IpAddress,
            Port = scale.Port,
            Protocol = scale.Protocol.ToString(),
            FramePrefixLength = scale.FramePrefixLength ?? 1,
            WeightLength = scale.WeightLength ?? 6,
            DecimalPlaces = scale.DecimalPlaces ?? 0,
            FrameTerminator = scale.FrameTerminator ?? "\n",
            FramePattern = scale.FramePattern,
            LogRawFrames = scale.LogRawFrames
        };
    }
}
```

- [x] **Step 4: Reescrever o endpoint WebSocket**

`SiagroB1.Web/Sockets/TruckScale/TruckScaleWebSocketEndpoint.cs`:

```csharp
namespace SiagroB1.Web.Sockets.TruckScale;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public static class TruckScaleWebSocketEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapTruckScaleWebSocket(this IEndpointRouteBuilder app)
    {
        // Canal de rede interna: fica na porta do Web e NÃO é exposto pelo Gateway. É isso que
        // dispensa autenticar o SiagroB1.Client.
        app.Map("/ws/truck-scale", HandleAsync);
    }

    private static async Task HandleAsync(HttpContext context)
    {
        var hub = context.RequestServices.GetRequiredService<TruckScaleHub>();
        var configProvider = context.RequestServices.GetRequiredService<ScaleConfigProvider>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("TruckScaleWebSocket");

        var scaleCode = context.Request.Query["truckScaleId"].ToString();

        if (string.IsNullOrEmpty(scaleCode) || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            return;
        }

        var config = await configProvider.GetAsync(scaleCode);

        if (config == null)
        {
            logger.LogWarning("Balança {ScaleCode} não cadastrada; conexão recusada.", scaleCode);
            context.Response.StatusCode = 404;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        hub.Add(scaleCode, socket);

        logger.LogInformation("Balança {ScaleCode} conectada.", scaleCode);

        try
        {
            await SendAsync(socket, new { action = "scale_config", data = config });

            var buffer = new byte[4096];

            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var message = JsonSerializer.Deserialize<ScaleMessage>(json, JsonOptions);

                switch (message?.Action)
                {
                    case "weight_tick" when message.Data?.Weight is { } weight:
                        hub.PushWeight(scaleCode, weight);
                        break;

                    case "scale_status" when message.Data?.Online == false:
                        hub.SetOffline(scaleCode);
                        break;
                }
            }
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "Conexão da balança {ScaleCode} caiu.", scaleCode);
        }
        finally
        {
            hub.Remove(scaleCode);

            if (socket.State != WebSocketState.Closed && socket.State != WebSocketState.Aborted)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        }
    }

    private static async Task SendAsync(WebSocket socket, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
```

- [x] **Step 5: Criar o `ScalesController` (SSE e captura)**

`SiagroB1.Web/Controllers/ScalesController.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Commons.Scales;
using SiagroB1.Web.Sockets.TruckScale;

namespace SiagroB1.Web.Controllers;

[ApiController]
[Authorize]
[Route("scales")]
public class ScalesController(
    TruckScaleHub hub,
    CaptureStore captures,
    ILogger<ScalesController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Peso ao vivo. Emite a cada 250 ms enquanto a tela estiver aberta; o navegador reconecta
    /// sozinho pelo EventSource quando a conexão cai.
    /// </summary>
    [HttpGet("{code}/live")]
    public async Task Live([FromRoute] string code, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        // Proxies intermediários (YARP, nginx) precisam ser instruídos a não segurar o corpo,
        // senão o peso só chega ao navegador quando o buffer enche.
        Response.Headers["X-Accel-Buffering"] = "no";

        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        while (!cancellationToken.IsCancellationRequested)
        {
            var live = hub.GetLive(code);

            await Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(live, JsonOptions)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            await Task.Delay(250, cancellationToken);
        }
    }

    /// <summary>
    /// Aguarda o peso estabilizar e emite o comprovante. O peso nasce aqui, no servidor - é o que
    /// permite ao serviço de pesagem distinguir peso capturado de peso digitado.
    /// </summary>
    [HttpPost("{code}/capture")]
    public async Task<IActionResult> Capture([FromRoute] string code, CancellationToken cancellationToken)
    {
        var username = User.Identity?.Name;

        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        if (!hub.IsConnected(code))
            return Conflict("Balança offline. Verifique o serviço de captura da balança.");

        var deadline = DateTime.Now.AddSeconds(30);

        while (DateTime.Now < deadline && !cancellationToken.IsCancellationRequested)
        {
            var live = hub.GetLive(code);

            if (!live.Online)
                return Conflict("Balança offline. Verifique o serviço de captura da balança.");

            if (live.Stable)
            {
                var capture = captures.Create(code, live.Weight, username, DateTime.Now);

                logger.LogInformation(
                    "Peso capturado na balança {ScaleCode} por {Username}: {Weight} kg.",
                    code, username, live.Weight);

                return Ok(new { captureId = capture.CaptureId, weight = capture.Weight });
            }

            await Task.Delay(250, cancellationToken);
        }

        return StatusCode(StatusCodes.Status408RequestTimeout,
            "O peso não estabilizou. Aguarde o veículo parar e tente novamente.");
    }
}
```

- [x] **Step 6: Registrar os serviços**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`:

```csharp
        // Janela de 3 s com no mínimo 5 amostras; sem leitura por 2 s a balança é dada como offline.
        services.AddSingleton(new LiveReadingStore(
            window: TimeSpan.FromSeconds(3),
            minimumSamples: 5,
            offlineAfter: TimeSpan.FromSeconds(2)));

        services.AddSingleton<TruckScaleHub>();
        services.AddScoped<ScaleConfigProvider>();
```

Adicione os `using SiagroB1.Commons.Scales;` necessários.

- [x] **Step 7: Compilar e verificar o endpoint manualmente**

Run: `dotnet build SiagroB1.sln`
Expected: build sem erro.

Suba o `SiagroB1.Web` e confirme que o SSE responde (sem balança conectada ele deve emitir `online:false` continuamente):

```bash
curl -N http://localhost:50000/scales/TS01/live
```

Expected: linhas `data: {"weight":0,"stable":false,"online":false}` a cada 250 ms. Interrompa com Ctrl+C.
Se responder 401, você chamou direto o Web sem cookie — nesse caso teste depois, pelo Gateway, na Task 10.

- [x] **Step 8: Stage**

```bash
git -C . add SiagroB1.Web/Sockets SiagroB1.Web/Controllers/ScalesController.cs SiagroB1.Web/Extensions
```

---

### Task 7: Entity set `UserTruckScales` e rota do Gateway

**Files:**
- Create: `SiagroB1.Application/Services/UserTruckScales/UserTruckScalesGetService.cs`
- Create: `SiagroB1.Application/Services/UserTruckScales/UserTruckScalesCreateService.cs`
- Create: `SiagroB1.Application/Services/UserTruckScales/UserTruckScalesUpdateService.cs`
- Create: `SiagroB1.Application/Services/UserTruckScales/UserTruckScalesDeleteService.cs`
- Create: `SiagroB1.Web/Controllers/UserTruckScalesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Modify: `SiagroB1.Gateway/appsettings.json`, `appsettings.Development.json`, `appsettings.Yokotobi.json`

**Interfaces:**
- Consumes: `UserTruckScale` (Task 3).
- Produces: `/odata/UserTruckScales` (GET/POST/PATCH/DELETE) e a rota `/scales/{**catch-all}` no Gateway.

- [x] **Step 1: Criar os serviços de CRUD**

Siga exatamente o padrão de um cadastro simples já existente — abra `SiagroB1.Application/Services/Permissions/` e espelhe a estrutura, trocando a entidade. O `UserTruckScalesGetService` precisa de:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.UserTruckScales;

public class UserTruckScalesGetService(IUnitOfWork db)
{
    /// <summary>Include da balança: a grade mostra o nome, não só o código.</summary>
    public IQueryable<UserTruckScale> QueryAll() =>
        db.Context.UserTruckScales.Include(x => x.TruckScale);

    public async Task<UserTruckScale?> GetByIdAsync(Guid key) =>
        await db.Context.UserTruckScales
            .Include(x => x.TruckScale)
            .FirstOrDefaultAsync(x => x.Id == key);
}
```

O `CreateService` deve recusar duplicidade com mensagem de negócio antes de o índice único estourar:

```csharp
        var duplicated = await db.Context.UserTruckScales
            .AnyAsync(x => x.Username == entity.Username && x.Purpose == entity.Purpose);

        if (duplicated)
            throw new DefaultException(
                "Este usuário já possui uma balança configurada para esta finalidade.");
```

- [x] **Step 2: Criar o controller OData**

`SiagroB1.Web/Controllers/UserTruckScalesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.UserTruckScales;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

[Route("odata/UserTruckScales")]
public class UserTruckScalesController(
    UserTruckScalesGetService getService,
    UserTruckScalesCreateService createService,
    UserTruckScalesUpdateService updateService,
    UserTruckScalesDeleteService deleteService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<UserTruckScale>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<UserTruckScale>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);

        return item == null ? NotFound() : Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] UserTruckScale entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity);
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return Created(entity);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromODataUri] Guid key, [FromBody] Delta<UserTruckScale> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var entity = await getService.GetByIdAsync(key);

        if (entity == null)
            return NotFound();

        try
        {
            patch.Patch(entity);

            await updateService.ExecuteAsync(key, entity);
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        var success = await deleteService.ExecuteAsync(key);

        return success ? NoContent() : NotFound();
    }
}
```

Confirme, ao escrever os serviços do Step 1, que os nomes dos métodos batem com estes (`ExecuteAsync` em Create/Update/Delete, `QueryAll`/`GetByIdAsync` no Get) — o padrão varia entre os cadastros antigos do projeto.

- [x] **Step 3: Registrar no EDM**

Em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, junto dos demais `EntitySet`:

```csharp
        modelBuilder.EntitySet<UserTruckScale>("UserTruckScales");
```

- [x] **Step 4: Registrar os serviços**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, junto dos demais cadastros:

```csharp
        services.AddScoped<UserTruckScalesGetService>();
        services.AddScoped<UserTruckScalesCreateService>();
        services.AddScoped<UserTruckScalesUpdateService>();
        services.AddScoped<UserTruckScalesDeleteService>();
```

- [x] **Step 5: Abrir a rota `/scales` no Gateway**

Nos três `appsettings` do Gateway (`appsettings.json`, `appsettings.Development.json`, `appsettings.Yokotobi.json`), dentro de `ReverseProxy.Routes` — só onde o bloco `ReverseProxy` já existir:

```json
      "scales-route": {
        "ClusterId": "backend",
        "Match": {
          "Path": "/scales/{**catch-all}"
        },
        "AuthorizationPolicy": "AuthenticatedOnly"
      },
```

- [x] **Step 6: Verificar o metadata e a rota**

Run: `dotnet build SiagroB1.sln`

Suba `SiagroB1.Web` e `SiagroB1.Gateway` e confirme:

```bash
curl -s http://localhost:50000/odata/\$metadata | grep -c UserTruckScale
```

Expected: número maior que zero (o tipo aparece no EDM).

- [x] **Step 7: Stage**

```bash
git -C . add SiagroB1.Application/Services/UserTruckScales SiagroB1.Web/Controllers \
  SiagroB1.Web/ODataConfig SiagroB1.Web/Extensions SiagroB1.Gateway
```

---

### Task 8: Reescrita do SiagroB1.Client

**Files:**
- Create: `SiagroB1.Client/Dtos/ScaleConfigMessage.cs`
- Create: `SiagroB1.Client/Readers/ScaleTcpConnection.cs`
- Create: `SiagroB1.Client/ScaleWorker.cs`
- Modify: `SiagroB1.Client/Program.cs`
- Modify: `SiagroB1.Client/Mock/MockScaleReader.cs`
- Modify: `SiagroB1.Client/SiagroB1.Client.csproj`
- Modify: `SiagroB1.Client/appsettings.json`, `appsettings.Development.json`
- Delete: `SiagroB1.Client/Worker.cs`, `SiagroB1.Client/Dtos/WsMessage.cs`, `SiagroB1.Client/Readers/TcpScaleReader.cs`, `SiagroB1.Client/Interfaces/IScaleReader.cs`

**Interfaces:**
- Consumes: `ScaleProtocolFactory`, `ScaleProtocolOptions`, `ScaleFrameBuffer` (Task 1); mensagens WS (Task 6).
- Produces: serviço que mantém uma conexão WS por balança e transmite `weight_tick`.

- [x] **Step 1: Referenciar o `SiagroB1.Commons` no Client**

Em `SiagroB1.Client/SiagroB1.Client.csproj`, adicione:

```xml
    <ItemGroup>
      <ProjectReference Include="..\SiagroB1.Commons\SiagroB1.Commons.csproj" />
    </ItemGroup>
```

- [x] **Step 2: Remover o desenho antigo**

```bash
git -C . rm SiagroB1.Client/Worker.cs SiagroB1.Client/Dtos/WsMessage.cs \
            SiagroB1.Client/Readers/TcpScaleReader.cs SiagroB1.Client/Interfaces/IScaleReader.cs
```

- [x] **Step 3: Criar o contrato de configuração**

`SiagroB1.Client/Dtos/ScaleConfigMessage.cs`:

```csharp
using SiagroB1.Commons.Scales;

namespace SiagroB1.Client.Dtos;

public sealed class ScaleConfigMessage
{
    public string? Action { get; set; }

    public ScaleConfigData? Data { get; set; }
}

public sealed class ScaleConfigData
{
    public string? Ip { get; set; }

    public int Port { get; set; }

    public string Protocol { get; set; } = "JundiaiBj850";

    public int FramePrefixLength { get; set; } = 1;

    public int WeightLength { get; set; } = 6;

    public int DecimalPlaces { get; set; }

    public string FrameTerminator { get; set; } = "\n";

    public string? FramePattern { get; set; }

    public bool LogRawFrames { get; set; }

    public ScaleProtocolOptions ToOptions() => new()
    {
        Protocol = Protocol,
        FramePrefixLength = FramePrefixLength,
        WeightLength = WeightLength,
        DecimalPlaces = DecimalPlaces,
        FrameTerminator = FrameTerminator,
        FramePattern = FramePattern
    };
}
```

- [x] **Step 4: Criar a conexão TCP com o indicador**

`SiagroB1.Client/Readers/ScaleTcpConnection.cs`:

```csharp
using System.Net.Sockets;
using System.Text;
using SiagroB1.Commons.Scales;

namespace SiagroB1.Client.Readers;

/// <summary>
/// Mantém o socket com o indicador e devolve cada peso lido pelo callback. Reconecta sozinho: a
/// queda do indicador não pode derrubar a conexão com o servidor.
/// </summary>
public sealed class ScaleTcpConnection(
    string host,
    int port,
    ScaleProtocolOptions options,
    bool logRawFrames,
    Action<int> onWeight,
    Action<bool> onConnectionChanged,
    ILogger logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var protocol = ScaleProtocolFactory.Create(options);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, ct);

                onConnectionChanged(true);
                logger.LogInformation("Indicador {Host}:{Port} conectado.", host, port);

                var stream = client.GetStream();
                var bytes = new byte[1024];
                var buffer = new ScaleFrameBuffer(options.FrameTerminator);

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var read = await stream.ReadAsync(bytes, ct);
                    if (read == 0)
                        break;

                    var chunk = Encoding.ASCII.GetString(bytes, 0, read);

                    foreach (var frame in buffer.Append(chunk))
                    {
                        if (logRawFrames)
                            logger.LogInformation("Frame cru: {Frame}", frame);

                        if (protocol.TryParse(frame, out var weight))
                            onWeight(weight);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha no indicador {Host}:{Port}.", host, port);
            }

            onConnectionChanged(false);

            try
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
```

- [x] **Step 5: Criar o worker por balança**

`SiagroB1.Client/ScaleWorker.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiagroB1.Client.Dtos;
using SiagroB1.Client.Readers;

namespace SiagroB1.Client;

/// <summary>
/// Uma balança, uma conexão. O Client é produtor: recebe a configuração do servidor e transmite o
/// peso continuamente, sem esperar por pedido.
/// </summary>
public class ScaleWorker(
    string scaleCode,
    IConfiguration config,
    ILogger<ScaleWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Intervalo de transmissão. 250 ms é imperceptível na balança e mantém o tráfego baixo.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private int _lastWeight;
    private bool _indicatorOnline;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var readerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            try
            {
                using var ws = new ClientWebSocket();

                var url = $"{config["WebSocketUrl"]}?truckScaleId={scaleCode}";
                await ws.ConnectAsync(new Uri(url), stoppingToken);

                logger.LogInformation("Balança {ScaleCode} conectada ao servidor.", scaleCode);

                var scaleConfig = await ReceiveConfigAsync(ws, stoppingToken);

                if (scaleConfig == null)
                    throw new InvalidOperationException("Configuração da balança não recebida.");

                var reader = CreateReader(scaleConfig, readerCts.Token);

                await Task.WhenAny(reader, StreamWeightAsync(ws, readerCts.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Conexão da balança {ScaleCode} caiu.", scaleCode);
            }
            finally
            {
                await readerCts.CancelAsync();
            }

            try
            {
                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private Task CreateReader(ScaleConfigData scaleConfig, CancellationToken ct)
    {
        if (config.GetValue<bool>("UseMockScale"))
        {
            return new Mock.MockScaleReader(w => _lastWeight = w, online => _indicatorOnline = online)
                .RunAsync(ct);
        }

        var connection = new ScaleTcpConnection(
            scaleConfig.Ip ?? "127.0.0.1",
            scaleConfig.Port,
            scaleConfig.ToOptions(),
            scaleConfig.LogRawFrames,
            weight => _lastWeight = weight,
            online => _indicatorOnline = online,
            logger);

        return connection.RunAsync(ct);
    }

    private async Task<ScaleConfigData?> ReceiveConfigAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, ct);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

        var message = JsonSerializer.Deserialize<ScaleConfigMessage>(json, JsonOptions);

        return message?.Action == "scale_config" ? message.Data : null;
    }

    private async Task StreamWeightAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var lastReportedOnline = true;

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            if (_indicatorOnline)
            {
                await SendAsync(ws, new { action = "weight_tick", data = new { weight = _lastWeight } }, ct);
                lastReportedOnline = true;
            }
            else if (lastReportedOnline)
            {
                await SendAsync(ws, new { action = "scale_status", data = new { online = false } }, ct);
                lastReportedOnline = false;
            }

            await Task.Delay(TickInterval, ct);
        }
    }

    private static async Task SendAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));

        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}
```

- [x] **Step 6: Reescrever o mock para estabilizar**

`SiagroB1.Client/Mock/MockScaleReader.cs`:

```csharp
namespace SiagroB1.Client.Mock;

/// <summary>
/// Balança simulada. Sobe até um alvo, ESTABILIZA por alguns segundos e depois muda de alvo - o
/// mock anterior subia para sempre e nunca estabilizava, então não exercitava a captura.
/// </summary>
public sealed class MockScaleReader(Action<int> onWeight, Action<bool> onConnectionChanged)
{
    private readonly Random _random = new();

    public async Task RunAsync(CancellationToken ct)
    {
        onConnectionChanged(true);

        var current = 0;
        var target = _random.Next(15000, 45000);
        var stableSince = DateTime.Now;

        while (!ct.IsCancellationRequested)
        {
            if (current < target)
            {
                current = Math.Min(current + 1500, target);
                stableSince = DateTime.Now;
            }
            else if (DateTime.Now - stableSince > TimeSpan.FromSeconds(20))
            {
                target = _random.Next(15000, 45000);
                current = 0;
            }

            onWeight(current);

            try
            {
                await Task.Delay(200, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        onConnectionChanged(false);
    }
}
```

- [x] **Step 7: Reescrever o `Program.cs` para N balanças**

```csharp
using SiagroB1.Client;

var builder = Host.CreateApplicationBuilder(args);

if (OperatingSystem.IsWindows())
    builder.Services.AddWindowsService();

if (OperatingSystem.IsLinux())
    builder.Services.AddSystemd();

// Uma instância do serviço atende N balanças: uma conexão WebSocket para cada.
var scaleCodes = builder.Configuration.GetSection("TruckScaleIds").Get<string[]>() ?? [];

if (scaleCodes.Length == 0)
    throw new InvalidOperationException("Configure TruckScaleIds no appsettings.");

foreach (var code in scaleCodes)
{
    builder.Services.AddSingleton<IHostedService>(sp => new ScaleWorker(
        code,
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<ScaleWorker>>()));
}

var host = builder.Build();
host.Run();
```

`appsettings.Development.json` passa a ser:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "TruckScaleIds": [ "TS01" ],
  "WebSocketUrl": "ws://localhost:50000/ws/truck-scale",
  "UseMockScale": true
}
```

Note que `ScaleTcp` sai: IP e porta agora vêm do cadastro.

- [x] **Step 8: Verificar de ponta a ponta com o mock**

Run: `dotnet build SiagroB1.sln`

Cadastre a balança `TS01` no banco (pela tela de Balanças ou por SQL direto), suba `SiagroB1.Web` e depois:

```bash
dotnet run --project SiagroB1.Client
```

Expected no log do Client: "Balança TS01 conectada ao servidor."
Expected no `curl -N http://localhost:50000/scales/TS01/live`: peso subindo e, ao parar de subir, `"stable":true`.

- [x] **Step 9: Stage**

```bash
git -C . add SiagroB1.Client
```

---

### Task 9: Cadastros no frontend

**Files (repo `siagro-b1-frontend`):**
- Modify: `webapp/view/truckScales/fragments/Form.fragment.xml`
- Modify: `webapp/view/veiculo/fragments/Form.fragment.xml`
- Create: `webapp/view/users/fragments/TruckScales.fragment.xml`
- Modify: `webapp/view/users/Edit.view.xml`
- Modify: `webapp/controller/users/Edit.controller.ts`
- Modify: `webapp/model/ServerRoutes.ts`

**Interfaces:**
- Consumes: `/odata/UserTruckScales`, campos novos de `/odata/TruckScales` e `/odata/Trucks` (Tasks 3 e 7).
- Produces: telas onde a balança, a tara e o vínculo usuário×balança são cadastrados.

- [x] **Step 1: Campos da balança**

Em `webapp/view/truckScales/fragments/Form.fragment.xml`, dentro de `<f:content>`, depois do campo Localização:

```xml
          <core:Title text="Conexão" emphasized="true" />
          <Label text="Endereço IP" />
          <Input value="{IpAddress}" editable="true" maxLength="50" />
          <Label text="Porta" />
          <Input
            value="{
              path: 'Port',
              type: 'sap.ui.model.type.Integer'
            }"
            editable="true" />
          <Label text="Protocolo" />
          <Select
            selectedKey="{
              path: 'Protocol',
              targetType: 'any'
            }"
            forceSelection="false">
            <core:ListItem key="JundiaiBj850" text="Jundiaí BJ850" />
            <core:ListItem key="Generic" text="Genérico (expressão regular)" />
          </Select>

          <core:Title text="Validação da Tara" emphasized="true" />
          <Label text="Validar tara" />
          <CheckBox
            selected="{
              path: 'ValidateTare',
              targetType: 'any'
            }" />
          <Label text="Tolerância (kg)" />
          <Input
            value="{
              path: 'TareToleranceKg',
              type: 'sap.ui.model.type.Integer'
            }"
            editable="true" />
```

E, num painel recolhido ao final do formulário — é aqui que o BJ850 real será calibrado:

```xml
          <core:Title text="Avançado (parsing do frame)" emphasized="true" />
          <Label text="Caracteres antes do peso" />
          <Input value="{ path: 'FramePrefixLength', type: 'sap.ui.model.type.Integer' }" />
          <Label text="Dígitos do peso" />
          <Input value="{ path: 'WeightLength', type: 'sap.ui.model.type.Integer' }" />
          <Label text="Casas decimais" />
          <Input value="{ path: 'DecimalPlaces', type: 'sap.ui.model.type.Integer' }" />
          <Label text="Expressão regular (protocolo genérico)" />
          <Input value="{FramePattern}" maxLength="200" />
          <Label text="Registrar frames no log" />
          <CheckBox selected="{ path: 'LogRawFrames', targetType: 'any' }" />
```

**Atenção:** `CheckBox` sem `targetType: 'any'` renderiza sempre marcado — o binding entrega string e o UI5 a considera verdadeira. Só quebra no navegador.

- [x] **Step 2: Tara no cadastro de veículo**

Em `webapp/view/veiculo/fragments/Form.fragment.xml`, depois do campo Modelo:

```xml
          <Label text="Tara (kg)" />
          <Input
            value="{
              path: 'TareWeight',
              type: 'sap.ui.model.type.Integer',
              formatOptions: {
                groupingEnabled: true,
                groupingSeparator: '.'
              }
            }"
            editable="true" />
```

Não marque `required` — o campo é opcional de propósito, e exigi-lo travaria a gravação dos veículos legados.

- [x] **Step 3: Grade de balanças do usuário**

Crie `webapp/view/users/fragments/TruckScales.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:t="sap.ui.table"
    xmlns:core="sap.ui.core"
>
  <t:Table
    id="userTruckScalesTable"
    class="sapUiSizeCondensed"
    alternateRowColors="true"
    enableBusyIndicator="true"
    enableSelectAll="false"
    selectionBehavior="Row"
    selectionMode="Single"
    busyIndicatorDelay="0"
    rows="{
      path: '/UserTruckScales',
      parameters: {
        $expand: 'TruckScale',
        $$ownRequest: true
      }
    }">
    <t:extension>
      <OverflowToolbar>
        <content>
          <Title text="Balanças" />
          <ToolbarSpacer />
          <Button text="Incluir" type="Transparent" icon="sap-icon://add" press=".onAddTruckScale" />
          <Button text="Remover" type="Transparent" icon="sap-icon://delete" press=".onRemoveTruckScale" />
        </content>
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <t:Column label="Finalidade">
        <t:template>
          <Select
            selectedKey="{ path: 'Purpose', targetType: 'any' }"
            forceSelection="false">
            <core:ListItem key="Opening" text="Abertura" />
            <core:ListItem key="Closing" text="Encerramento" />
          </Select>
        </t:template>
      </t:Column>
      <t:Column label="Balança">
        <t:template>
          <Input
            width="10rem"
            required="true"
            value="{TruckScaleCode}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openTruckScalesValueHelp">
            <customData>
              <core:CustomData key="descriptionProperty" value="TruckScale/Name" />
            </customData>
          </Input>
        </t:template>
      </t:Column>
      <t:Column label="Nome">
        <t:template>
          <Text text="{TruckScale/Name}" />
        </t:template>
      </t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

`$$ownRequest: true` é obrigatório: a grade não é filha da entidade `Users` (que vive no outro banco), então precisa da própria requisição.

- [x] **Step 4: Aba nova no cadastro de usuário**

Em `webapp/view/users/Edit.view.xml`, dentro do `IconTabBar`, depois do `IconTabFilter` "Perfis":

```xml
          <IconTabFilter text="Balanças">
            <content>
              <core:Fragment fragmentName="siagrob1.view.users.fragments.TruckScales" type="XML" />
            </content>
          </IconTabFilter>
```

Em `webapp/controller/users/Edit.controller.ts`, filtre a grade pelo usuário em edição e trate inclusão/remoção. O filtro do enum precisa ser montado como texto: `sap.ui.model.Filter` sobre enum estoura "Unsupported type".

```typescript
	/**
	 * A grade de balanças não é filha de Users - a entidade vive no banco da empresa, e o usuário
	 * no COMMON. Por isso ela tem binding próprio, filtrado pelo username depois que a entidade
	 * do usuário chega.
	 */
	private bindTruckScales(username: string): void {
		const table = this.byId("userTruckScalesTable") as Table;
		const binding = table.getBinding("rows") as ODataListBinding;

		void binding.changeParameters({
			$filter: `Username eq '${username.replace(/'/g, "''")}'`
		});
	}

	onAddTruckScale(): void {
		const username = this.getView().getBindingContext()?.getProperty("Username") as string;
		const table = this.byId("userTruckScalesTable") as Table;
		const binding = table.getBinding("rows") as ODataListBinding;

		binding.create({ Username: username, Purpose: "Opening" }, false, true, false);
	}

	onRemoveTruckScale(): void {
		const table = this.byId("userTruckScalesTable") as Table;
		const selected = table.getSelectedIndices();

		if (selected.length === 0) {
			MessageBox.alert("Selecione uma balança para remover.");
			return;
		}

		const context = table.getContextByIndex(selected[0]) as Context;
		const model = this.getView().getModel() as ODataModel;

		void context.delete(model.getUpdateGroupId());
	}
```

Chame `bindTruckScales` dentro do `dataReceived` do `bindElement` já existente no `editRouteMatched`, lendo o `Username` do contexto — antes disso o username ainda não chegou.

- [x] **Step 5: Value help de balanças**

Em `webapp/controller/common/CommonController.ts`, junto dos demais value helps:

```typescript
  openTruckScalesValueHelp(ev: Input$ValueHelpRequestEvent) {
    void this.applyValueHelp(ev, "TruckScalesSelectDialog", ["Name", "Code"], "Code");
  }
```

Crie `webapp/dialogs/fragments/TruckScalesSelectDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
>
  <TableSelectDialog
    title="Balanças"
    search=".onSearch"
    confirm=".onConfirm"
    growing="true"
    growingThreshold="50"
    items="{
      path: '/TruckScales',
      sorter: { path: 'Code' }
    }">
    <columns>
      <Column>
        <Text text="Código" />
      </Column>
      <Column>
        <Text text="Nome" />
      </Column>
      <Column>
        <Text text="Localização" />
      </Column>
    </columns>
    <ColumnListItem>
      <cells>
        <Text text="{Code}" />
        <Text text="{Name}" />
        <Text text="{Localization}" />
      </cells>
    </ColumnListItem>
  </TableSelectDialog>
</core:FragmentDefinition>
```

Abra `webapp/dialogs/fragments/PermissionsSelectDialog.fragment.xml` antes de gravar e alinhe os nomes dos handlers (`search`/`confirm`) com o que o `applyValueHelp` do `CommonController` espera — o contrato do `DialogHelper` é quem manda aqui.

- [x] **Step 6: Rotas novas**

Em `webapp/model/ServerRoutes.ts`:

```typescript
  truckScales: '/odata/TruckScales',
  userTruckScales: '/odata/UserTruckScales',

  // Captura de peso. Fora do /odata: são endpoints de streaming e de comando, roteados no
  // Gateway por /scales.
  scaleLive: (code: string): string => `/scales/${encodeURIComponent(code)}/live`,
  scaleCapture: (code: string): string => `/scales/${encodeURIComponent(code)}/capture`,
```

- [x] **Step 7: Verificar e stage**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck`
Expected: sem erro.
Run: `yarn lint`
Expected: sem erro novo.

```bash
git -C ../siagro-b1-frontend add webapp/view/truckScales webapp/view/veiculo webapp/view/users \
  webapp/controller/users webapp/controller/common webapp/dialogs/fragments webapp/model/ServerRoutes.ts
```

---

### Task 10: Captura na tela de pesagem

**Files (repo `siagro-b1-frontend`):**
- Create: `webapp/types/ScaleLive.ts`
- Create: `webapp/services/ScaleLiveService.ts`
- Create: `webapp/view/weighingTicket/fragments/WeighingCapture.fragment.xml`
- Modify: `webapp/controller/weighingTicket/GenericController.ts`
- Modify: `webapp/view/weighingTicket/fragments/FirstWeighingForm.fragment.xml`, `SecondWeighingForm.fragment.xml`, `Weighing.fragment.xml`
- Modify: `webapp/controller/weighingTicket/FirstWeighing.controller.ts`, `SecondWeighing.controller.ts`, `Main.controller.ts`
- Modify: `webapp/services/SessionService.ts`, `webapp/types/UserIdentity.ts`
- Modify: `ui5.yaml`

**Interfaces:**
- Consumes: `/scales/{code}/live`, `/scales/{code}/capture` (Task 6); `Permissions` no `/status` (Task 4); `/odata/UserTruckScales` (Task 7).
- Produces: `ui>/scaleCode`, `ui>/canTypeWeight`, `ui>/captureId`, `ui>/liveWeight`, `ui>/liveStable`, `ui>/liveOnline`.

- [x] **Step 1: Proxy de desenvolvimento**

Em `ui5.yaml`, depois do bloco `/reports`:

```yaml
    - name: ui5-middleware-simpleproxy
      afterMiddleware: compression
      mountPath: /scales
      configuration:
        baseUri: "http://localhost:5246/scales"
```

- [x] **Step 2: Permissões na sessão**

Em `webapp/types/UserIdentity.ts`, adicione a `UserIdentity`:

```typescript
  /** Permissões efetivas. A tela usa para não oferecer o que o servidor recusaria. */
  permissions?: string[];
```

Em `webapp/services/SessionService.ts`, dentro de `applyUserIdentity`:

```typescript
    sessionModel.setProperty("/permissions", identity?.permissions ?? []);
```

e um método público:

```typescript
  /** Administrador passa por cima de qualquer permissão, como no servidor. */
  public hasPermission(code: string): boolean {
    const sessionModel = this.getSessionModel();

    if (sessionModel.getProperty("/isAdmin") === true) {
      return true;
    }

    const permissions = (sessionModel.getProperty("/permissions") ?? []) as string[];

    return permissions.includes(code);
  }
```

- [x] **Step 3: Tipos e serviço de peso ao vivo**

`webapp/types/ScaleLive.ts`:

```typescript
/** Uma amostra do peso ao vivo, como o servidor a publica no SSE. */
export type LiveWeight = {
  weight: number;
  stable: boolean;
  online: boolean;
};

/** Resposta de POST /scales/{code}/capture. */
export type CaptureResult = {
  captureId: string;
  weight: number;
};
```

`webapp/services/ScaleLiveService.ts`:

```typescript
import ServerRoutes from "siagrob1/model/ServerRoutes";
import { LiveWeight } from "siagrob1/types/ScaleLive";

/**
 * Assinatura do peso ao vivo. O EventSource reconecta sozinho quando a conexão cai; o que ele
 * NÃO faz é fechar sozinho ao sair da tela - por isso `unsubscribe` é obrigatório no onExit,
 * senão sobra uma conexão aberta por operador.
 */
class ScaleLiveService {
  private source?: EventSource;

  public subscribe(scaleCode: string, onWeight: (live: LiveWeight) => void): void {
    this.unsubscribe();

    const source = new EventSource(ServerRoutes.scaleLive(scaleCode));

    source.onmessage = (event: MessageEvent<string>): void => {
      try {
        onWeight(JSON.parse(event.data) as LiveWeight);
      } catch (error) {
        console.warn("Leitura de peso inválida.", error);
      }
    };

    source.onerror = (): void => onWeight({ weight: 0, stable: false, online: false });

    this.source = source;
  }

  public unsubscribe(): void {
    this.source?.close();
    this.source = undefined;
  }
}

export default new ScaleLiveService();
```

- [x] **Step 4: Fragmento de captura**

`webapp/view/weighingTicket/fragments/WeighingCapture.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:l="sap.ui.layout"
    xmlns:core="sap.ui.core"
>
  <VBox class="sapUiSmallMargin">
    <ObjectNumber
      number="{ui>/liveWeight}"
      unit="KG"
      emphasized="true"
      state="{= ${ui>/liveStable} ? 'Success' : 'Warning' }"
      visible="{ui>/scaleConfigured}" />
    <ObjectStatus
      text="{ui>/liveStatusText}"
      state="{ui>/liveStatusState}" />
    <Button
      text="Usar este peso"
      icon="sap-icon://accept"
      type="Emphasized"
      enabled="{= ${ui>/liveOnline} &amp;&amp; ${ui>/liveStable} }"
      visible="{ui>/scaleConfigured}"
      press=".onUseCapturedWeight" />
    <MessageStrip
      text="Nenhuma balança configurada para esta etapa. Peça ao administrador para configurá-la no seu usuário."
      type="Warning"
      showIcon="true"
      visible="{= !${ui>/scaleConfigured} }" />
  </VBox>
</core:FragmentDefinition>
```

- [x] **Step 5: Ciclo de vida da captura no `GenericController`**

Em `webapp/controller/weighingTicket/GenericController.ts`, adicione:

```typescript
  /**
   * Prepara a captura para a etapa. Resolve a balança do usuário, decide se ele pode digitar e
   * liga o peso ao vivo. Chamar no routeMatched de cada tela e ao abrir o diálogo da lista.
   */
  async startWeighingCapture(purpose: "Opening" | "Closing"): Promise<void> {
    const uiModel = this.getModel("ui") as JSONModel;

    uiModel.setProperty("/canTypeWeight", SessionService.hasPermission("WEIGHING_MANUAL_ENTRY"));
    uiModel.setProperty("/captureId", null);
    uiModel.setProperty("/liveWeight", 0);
    uiModel.setProperty("/liveStable", false);
    uiModel.setProperty("/liveOnline", false);
    uiModel.setProperty("/liveStatusText", "Localizando a balança...");
    uiModel.setProperty("/liveStatusState", "None");

    const scaleCode = await this.resolveUserScaleCode(purpose);

    uiModel.setProperty("/scaleCode", scaleCode);
    uiModel.setProperty("/scaleConfigured", !!scaleCode);

    if (!scaleCode) {
      uiModel.setProperty("/liveStatusText", "Sem balança configurada");
      uiModel.setProperty("/liveStatusState", "Warning");
      return;
    }

    ScaleLiveService.subscribe(scaleCode, live => {
      uiModel.setProperty("/liveWeight", live.weight);
      uiModel.setProperty("/liveStable", live.stable);
      uiModel.setProperty("/liveOnline", live.online);
      uiModel.setProperty("/liveStatusText",
        !live.online ? "Balança offline" : live.stable ? "Peso estável" : "Estabilizando...");
      uiModel.setProperty("/liveStatusState",
        !live.online ? "Error" : live.stable ? "Success" : "Warning");
    });
  }

  /** Obrigatório: o EventSource não fecha sozinho ao sair da tela. */
  stopWeighingCapture(): void {
    ScaleLiveService.unsubscribe();
  }

  /**
   * Busca a balança do usuário para a etapa. O $filter é montado como texto porque
   * `sap.ui.model.Filter` sobre enum estoura "Unsupported type".
   */
  private async resolveUserScaleCode(purpose: "Opening" | "Closing"): Promise<string | null> {
    const username = (this.getModel("sessionModel") as JSONModel).getProperty("/userName") as string;

    if (!username) {
      return null;
    }

    const model = this.getView().getModel() as ODataModel;
    const binding = model.bindList("/UserTruckScales", null, [], [], {
      $filter: `Username eq '${username.replace(/'/g, "''")}' and Purpose eq '${purpose}'`
    });

    const contexts = await binding.requestContexts(0, 1);

    return contexts.length > 0 ? contexts[0].getProperty("TruckScaleCode") as string : null;
  }

  /** Pede a captura ao servidor e guarda o comprovante junto do peso. */
  async onUseCapturedWeight(): Promise<void> {
    const uiModel = this.getModel("ui") as JSONModel;
    const scaleCode = uiModel.getProperty("/scaleCode") as string;

    try {
      this.setBusy(true);

      const result = await new RequestModel()
        .post(ServerRoutes.scaleCapture(scaleCode), {}) as CaptureResult;

      uiModel.setProperty("/captureId", result.captureId);
      this.applyCapturedWeight(result.weight);

      MessageToast.show(`Peso capturado: ${result.weight.toLocaleString("pt-BR")} kg`);
    } catch (error) {
      MessageBox.error(this.readCaptureError(error));
    } finally {
      this.setBusy(false);
    }
  }

  /**
   * Cada tela grava o peso onde ele mora: na entidade (telas dedicadas) ou no viewModel
   * (diálogo da lista). Sobrescrever é obrigatório.
   */
  protected applyCapturedWeight(_weight: number): void {
    throw new Error("applyCapturedWeight não implementado nesta tela.");
  }

  private readCaptureError(error: unknown): string {
    const xhr = error as { responseText?: string; status?: number };

    if (xhr?.responseText) {
      return xhr.responseText;
    }

    return "Não foi possível capturar o peso da balança.";
  }
```

Adicione os imports: `JSONModel`, `ODataModel`, `SessionService`, `ScaleLiveService`, `RequestModel`, `ServerRoutes`, `CaptureResult`, `MessageToast`, `MessageBox`.

- [x] **Step 6: Usar o fragmento nas três telas**

Nos fragmentos `FirstWeighingForm.fragment.xml` e `SecondWeighingForm.fragment.xml`, substitua o `<Button text="Capturar Peso" ... />` (que hoje não tem `press`) por:

```xml
          <core:Fragment fragmentName="siagrob1.view.weighingTicket.fragments.WeighingCapture" type="XML" />
```

e torne o input condicional à permissão — em `FirstWeighingForm`:

```xml
          <Input
            description="KG"
            editable="{ui>/canTypeWeight}"
            value="{
              path: 'FirstWeighValue',
              type: 'sap.ui.model.type.Integer',
              formatOptions: {
                groupingEnabled: true,
                groupingSeparator: '.'
              }
            }" />
```

Em `SecondWeighingForm`, o mesmo com `SecondWeighValue`.

Em `Weighing.fragment.xml` (o diálogo da lista), troque os dois botões "Capturar Peso" pelo fragmento e aplique `editable="{ui>/canTypeWeight}"` nos dois inputs.

- [x] **Step 7: Ligar as telas**

Em `FirstWeighing.controller.ts`:

```typescript
  onExit(): void {
    this.stopWeighingCapture();
  }

  protected applyCapturedWeight(weight: number): void {
    const ctx = this.getView().getBindingContext() as Context;
    void ctx?.setProperty("FirstWeighValue", weight);
  }
```

No `routeMatched`, depois do `bindElement`, adicione `void this.startWeighingCapture("Opening");`.

No `onSave`, passe o comprovante:

```typescript
      action.setParameter("CaptureId", (this.getModel("ui") as JSONModel).getProperty("/captureId"));
```

`SecondWeighing.controller.ts` recebe o equivalente com `"Closing"` e `SecondWeighValue`. Aproveite para remover o código morto depois do `return` no `onSave` — há um bloco inteiro de `submitBatch` inalcançável.

Em `Main.controller.ts`, chame `void this.startWeighingCapture(sOperation === "FW" ? "Opening" : "Closing");` no `openWeighingDialog`, `this.stopWeighingCapture()` no `onCloseDlg`, implemente `applyCapturedWeight` gravando em `viewModel>/FirstWeighValue` ou `/SecondWeighValue` conforme a operação aberta, e passe `CaptureId` nas duas ações.

- [x] **Step 8: Verificar e stage**

Run: `yarn ts-typecheck`
Expected: sem erro.
Run: `yarn lint`
Expected: sem erro novo.

```bash
git -C ../siagro-b1-frontend add webapp ui5.yaml
```

---

### Task 11: Verificação de ponta a ponta no navegador

Nenhuma das armadilhas de binding do UI5 aparece nos gates — elas passam no `ts-typecheck` e no `lint` e só quebram no navegador. Esta task não é opcional.

**Files:** nenhum, salvo correções que a verificação exigir.

- [x] **Step 1: Subir a stack**

Backend (perfil `yktb`): `SiagroB1.Web` e `SiagroB1.Gateway`.
Client: `dotnet run --project SiagroB1.Client` com `UseMockScale: true`.
Frontend: `yarn start:dev`.
Login: `admin` / `1234`.

- [x] **Step 2: Cadastrar a configuração**

Pelo menu, cadastre/edite a balança `TS01` com IP, porta, protocolo Jundiaí BJ850, "Validar tara" ligado e tolerância 200 kg. Confirme que os campos gravam e voltam corretos ao reabrir a tela — em especial os `CheckBox`, que sem `targetType: 'any'` aparecem sempre marcados.

Cadastre a tara de um veículo. Configure, no cadastro do usuário `admin`, as balanças de Abertura e Encerramento.

- [x] **Step 3: Capturar como administrador**

Abra a 1ª pesagem de um romaneio. Confirme:
- o peso sobe na tela sozinho (o mock leva alguns segundos para chegar ao alvo);
- ao parar de subir, o status vira "Peso estável" e o botão "Usar este peso" habilita;
- clicar preenche o campo de peso;
- o campo de peso é editável (admin pode digitar);
- salvar grava e navega de volta.

Confirme no banco que `FirstWeighScaleCode` e `FirstWeighCaptured` foram gravados.

- [x] **Step 4: Verificar a restrição de digitação**

Crie (ou use) um usuário sem a permissão `WEIGHING_MANUAL_ENTRY` e sem `IsAdmin`, com balanças configuradas. Entrando com ele:
- o campo de peso deve estar bloqueado;
- a captura deve funcionar normalmente;
- tentar salvar sem capturar (limpando o `captureId` pelo console do navegador, para simular bypass) deve voltar com "O peso deve ser capturado da balança."

- [x] **Step 5: Verificar a tara**

Com "Validar tara" ligado e um veículo cuja tara cadastrada seja maior que o peso capturado + tolerância, confirme a mensagem de bloqueio citando os dois valores. Depois, com um veículo sem tara cadastrada, confirme a mensagem "Caminhão sem tara cadastrada".

- [x] **Step 6: Verificar a balança offline**

Pare o `SiagroB1.Client`. Em até ~2 s a tela deve mostrar "Balança offline" e desabilitar o botão. Suba o Client de novo e confirme que a tela volta a mostrar o peso sem recarregar a página.

- [x] **Step 7: Verificar o fechamento do SSE**

Com a tela de pesagem aberta, navegue para outra tela e confirme na aba Network do navegador que a conexão `/scales/TS01/live` foi encerrada. Uma conexão que sobrevive à navegação é vazamento.

- [x] **Step 8: Verificar o diálogo da lista**

Repita a captura pelo diálogo da lista de romaneios (botões de 1ª e 2ª pesagem no `Main`), que usa o mesmo fragmento por outro caminho.

- [x] **Step 9: Rodar a suíte e stage final**

Run (em `siagro-b1-backend/`): `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`
Expected: build sem erro, todos os testes passando.

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck && yarn lint`
Expected: sem erro.

```bash
git -C . add -A
git -C ../siagro-b1-frontend add -A
git -C . status --short
git -C ../siagro-b1-frontend status --short
```

Confirme que nada ficou como untracked e **não commite** — os commits são feitos manualmente pelo usuário.
