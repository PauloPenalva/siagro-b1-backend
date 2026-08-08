# Captura de peso da balança rodoviária

Data: 07/08/2026
Status: desenho validado, pendente de plano de implementação

## Problema

O `SiagroB1.Client` existe para ler o peso do indicador da balança rodoviária e disponibilizá-lo às
telas de 1ª e 2ª pesagem (`/weighing-tickets/{id}/first-weighing` e `/second-weighing`), mas a
funcionalidade está pela metade:

- Os botões "Capturar Peso" dos fragmentos `FirstWeighingForm`, `SecondWeighingForm` e `Weighing`
  não têm `press` — não existe nenhum código de captura no frontend.
- O endpoint de captura (`POST /api/TruckScale/{code}/capture`) não é roteado pelo Gateway (que só
  encaminha `/odata`, `/reports` e `/security`) nem pelo proxy de desenvolvimento do `ui5.yaml`, e
  está sem autenticação. O navegador não tem como alcançá-lo.
- Não há vínculo entre o romaneio, o usuário e **qual** balança usar. `WEIGHING_TICKETS` não tem
  balança; `TRUCK_SCALES` guarda apenas `Code`, `Name` e `Localization` — nem IP, nem porta, nem
  protocolo.
- `TcpScaleReader` pega o primeiro número da linha com uma regex, ignorando sinal, casas decimais e
  estabilidade. Não há nada específico do Jundiaí BJ850 nem seleção de protocolo por balança.
- `TruckScaleWebSocketConnectionManager` usa um `Dictionary` não sincronizado — falha real assim que
  houver mais de uma balança. `stable = true` está chumbado no Client.
- As tabelas `PERMISSIONS` / `ROLE_PERMISSIONS` e suas telas de CRUD existem, mas **nenhum código do
  sistema consome permissão**; o frontend só conhece `isAdmin`.

Objetivo: concluir a captura de ponta a ponta, com o primeiro modelo suportado sendo o **Jundiaí
BJ850** (extensível a outros protocolos), e impedir que determinados usuários digitem o peso — para
eles, só captura. Administradores podem digitar ou capturar.

## Decisões tomadas

| Assunto | Decisão |
|---|---|
| Qual balança usar | Amarrada ao **usuário**, com balança distinta por finalidade: Abertura (1ª pesagem) e Encerramento (2ª). Onde há uma só balança, informa-se a mesma nas duas finalidades. |
| Cardinalidade | Uma balança por (usuário, finalidade). Usuário sem configuração para a etapa não captura. |
| Conexão física | TCP/IP (indicador Ethernet ou conversor serial-ethernet). Não haverá leitura serial direta. |
| Protocolo BJ850 | Preset baseado no padrão encontrado no projeto Tagui (ASCII contínuo, 6 dígitos a partir da posição 1, terminador CR/LF), com parâmetros sobrescrevíveis no cadastro e log de frames crus para calibrar em campo. |
| Onde fica a configuração da balança | No cadastro (`TRUCK_SCALES`). O Client recebe a configuração do servidor ao conectar; um Client atende N balanças. |
| Experiência de captura | Peso ao vivo na tela com espera de estabilidade, e botão "Usar este peso". |
| Canal do peso ao vivo | SSE (Server-Sent Events). |
| Validação da tara | Configurável por balança (liga/desliga + tolerância em kg). Vale para **as duas** pesagens: nenhum peso lido pode ser menor que `tara do caminhão − tolerância`. |
| Caminhão sem tara | Bloqueia a pesagem, quando a validação está ligada. |
| Restrição de digitação | Permissão `WEIGHING_MANUAL_ENTRY` sobre as tabelas de permissão existentes. Admin sempre pode. |
| Rigor da restrição | O servidor exige **comprovante de captura** de quem não tem a permissão — não basta bloquear o campo na tela. |

## Arquitetura

A inversão central: hoje o Client é passivo (só responde a `capture_weight`). Ele passa a ser
**produtor** — transmite o peso continuamente, e o **servidor** vira dono da leitura corrente, do
cálculo de estabilidade e do comprovante de captura. Isso elimina o ping-pong por captura, viabiliza
o peso ao vivo e é o que torna o comprovante confiável: o peso nasce no servidor, não no navegador.

```
Indicador BJ850 --TCP--> SiagroB1.Client --WS (1 por balança)--> SiagroB1.Web
                                                                   |  LiveReadingStore (memória)
                                                                   |  StabilityDetector
                                                                   |  CaptureStore (comprovantes)
                                                           SSE /scales/{code}/live
                                                                   v
                                     Gateway (rota nova) --> navegador (fragmento de captura)
```

### SiagroB1.Client

Uma instância atende N balanças.

- `appsettings`: `WebSocketUrl` e `TruckScaleIds: ["TS01", "TS02"]`. Nada de IP, porta ou protocolo —
  eles vêm do servidor.
- Abre uma conexão WebSocket por balança. Ao conectar, o Web responde `scale_config` (IP, porta,
  protocolo e parâmetros de parsing lidos de `TRUCK_SCALES`); o Client então abre/reabre o socket TCP
  daquela balança.
- Envia `weight_tick` a cada ~250 ms com `{weight, rawFrame?, timestamp}` e `scale_status` quando o
  TCP do indicador cai ou volta.
- Reconexão com backoff nos dois níveis (WebSocket com o Web e TCP com o indicador).
- O `capture_weight` / `weight_result` do desenho atual **deixa de existir**: a captura passa a ser
  resolvida inteiramente no servidor, sobre o fluxo de leituras.

Protocolos plugáveis:

- `IScaleProtocol.TryParse(frame) → ScaleReading?`.
- Preset `JundiaiBj850`: ASCII, terminador CR/LF, 6 dígitos a partir da posição 1, sem casas
  decimais — com prefixo, tamanho, casas decimais e terminador sobrescrevíveis pelo cadastro.
- Preset `Generic`: extração por expressão regular configurável, para o próximo modelo.
- `LogRawFrames` grava os frames crus no log, que é como o BJ850 real será calibrado sem recompilar.

### SiagroB1.Web

- `TruckScaleHub` substitui o `TruckScaleWebSocketConnectionManager`: conexões e `LiveReading` por
  balança em `ConcurrentDictionary` (o `Dictionary` atual é um defeito real sob duas balanças).
- `StabilityDetector`: peso estável = todas as leituras da janela iguais entre si, com um mínimo de
  leituras na janela (padrão: janela de 3 s, mínimo de 5 leituras). Qualquer leitura diferente
  reinicia a janela. Sem leitura nova por mais de 2 s, a balança é considerada offline. O
  `stable = true` chumbado no Client desaparece.
- O peso trafega e é gravado em **quilos inteiros**: o parser aplica as casas decimais do protocolo e
  arredonda na leitura, de modo que a comparação entre o comprovante e o `Value` da ação seja exata.
- `GET /scales/{code}/live` — SSE autenticado, emitindo `{weight, stable, online}`. Sem tráfego
  enquanto nenhuma tela está aberta.
- `POST /scales/{code}/capture` — autenticado. Espera o peso estabilizar (timeout ~30 s), registra um
  comprovante `{captureId, scaleCode, weight, username, expiraEm}` em cache de memória (TTL ~10 min,
  uso único) e devolve `{captureId, weight}`.
- `/ws/truck-scale` **continua exclusivo do Web (porta 50000) e não é exposto pelo Gateway** — é canal
  de rede interna, e é isso que dispensa autenticar o Client.
- O `TruckScaleCaptureController` atual (`POST /api/TruckScale/{code}/capture`) é **removido**, junto
  do par `capture_weight` / `weight_result` e dos `Console.WriteLine` de depuração espalhados pelo
  `WsMessageHandler` e pelo `PendingRequestStore`. A rota `/api` nunca foi alcançável pelo navegador.
- O Gateway ganha a rota `/scales/{**catch-all}` → cluster `backend`, com `AuthorizationPolicy:
  AuthenticatedOnly` e sem buffering de resposta (senão o SSE não flui). O `ui5.yaml` ganha o proxy
  equivalente para o desenvolvimento.

## Modelo de dados

### `TRUCK_SCALES` (banco da empresa)

Novas colunas, todas editáveis na tela de Balanças:

- Conexão: `IpAddress`, `Port`, `Protocol` (`JundiaiBj850` | `Generic`).
- Parsing (nulos, sobrescrevem o preset): `FramePrefixLength`, `WeightLength`, `DecimalPlaces`,
  `FrameTerminator`, `FramePattern` (regex, usado pelo `Generic`).
- Tara: `ValidateTare` (bool) e `TareToleranceKg` (int).
- `LogRawFrames` (bool) — modo diagnóstico.

### `TRUCKS`

- `TareWeight` (kg, **nulo**).

Fica nulo e não obrigatório no formulário de propósito: torná-lo obrigatório travaria a gravação dos
caminhões legados que não têm tara. Quem cobra a tara é a validação da pesagem, e só quando a balança
pede.

### `USER_TRUCK_SCALES` (nova, banco da empresa)

- `Id` (Guid), `Username`, `TruckScaleCode` (FK → `TRUCK_SCALES`), `Purpose`
  (`Opening` | `Closing`, exibidos como "Abertura" e "Encerramento").
- Índice único `(Username, Purpose)`.

Sem FK para `USERS`: essa tabela vive no banco COMMON e a balança no banco da empresa. A chave é o
`Username` (e não o `UserId`) porque é o que a API tem em mãos (`User.Identity.Name`) e é o mesmo
padrão que `WEIGHING_TICKETS.FirstWeighUsername` já adota. Custo assumido: renomear um usuário órfã a
configuração dele.

### `WEIGHING_TICKETS`

Auditoria da origem do peso:

- `FirstWeighScaleCode` / `SecondWeighScaleCode` — colunas simples, sem FK e sem navegação, como
  `FreightUmCode` já faz.
- `FirstWeighCaptured` / `SecondWeighCaptured` (bool) — distingue peso capturado de peso digitado.

### `PERMISSIONS` (banco COMMON)

- Seed de `WEIGHING_MANUAL_ENTRY`, descrição "Digitar o peso manualmente na pesagem".

## Permissão e comprovante de captura

As tabelas de permissão existem mas nada as consome; é preciso ligar o fio:

- `/security/auth/login` e `/security/auth/status` passam a devolver `Permissions: string[]` — as
  permissões efetivas do usuário (perfis → papéis → permissões).
- `SessionService` publica em `sessionModel>/permissions` e expõe `hasPermission(code)`.
- No servidor, `WeighingTicketsFirstWeighingService` e `WeighingTicketsSecondWeighingService`
  resolvem `podeDigitar = IsAdmin || tem WEIGHING_MANUAL_ENTRY`. A leitura das permissões entra por
  uma interface (`IUserPermissions`, implementada sobre o `CommonDbContext`), de modo que a regra
  continue morando no serviço, junto dos guards que já existem lá.

Regras do comprovante, nas duas ações de pesagem:

- Sem a permissão, `CaptureId` é **obrigatório**. O comprovante precisa existir, não ter sido usado,
  pertencer ao mesmo usuário e ter peso igual ao `Value` enviado. É consumido no uso.
- Com a permissão, `CaptureId` é opcional; se vier, também é validado e consumido — é o que alimenta
  `FirstWeighCaptured` / `SecondWeighCaptured` e `...ScaleCode`.

## Validação da tara

Quando a balança usada tem `ValidateTare` ligado, valendo para as duas pesagens:

- Caminhão sem `TareWeight` → bloqueia: "Caminhão sem tara cadastrada".
- `Value < TareWeight − TareToleranceKg` → bloqueia, mostrando o peso lido e a tara cadastrada.

A balança considerada é a do comprovante. Na digitação por um administrador, é a balança configurada
para aquela etapa. Se nenhuma balança puder ser determinada, a validação não incide.

## Frontend

O peso é capturado em três lugares hoje: o diálogo da lista (`Main` → `Weighing.fragment`), a tela de
1ª pesagem e a de 2ª. Em vez de triplicar o código:

- Fragmento compartilhado `view/weighingTicket/fragments/WeighingCapture.fragment.xml`.
- Métodos de captura no `GenericController` de `weighingTicket`, que as três telas já herdam.
- Serviço `ScaleLiveService` encapsulando o `EventSource` (assinar, desassinar, estado offline).

O fragmento mostra o peso ao vivo (grande, em KG), um status — *Balança offline* / *Estabilizando…* /
*Estável* — e o botão **Usar este peso**, habilitado apenas com balança online e peso estável. O
input do peso fica `editable="{ui>/canTypeWeight}"`. Sem balança configurada para a etapa, o bloco
aparece desabilitado explicando o motivo, em vez de sumir sem explicação.

Ciclo de vida do SSE: abre ao entrar na tela ou abrir o diálogo, fecha no `onExit` e no fechamento do
diálogo. Sem isso sobra uma conexão aberta por operador.

Telas de cadastro:

- **Balanças**: IP, Porta, Protocolo, Validar tara, Tolerância (kg); os parâmetros de parsing num
  painel "Avançado" recolhido, que é onde o BJ850 será calibrado.
- **Caminhões**: Tara (kg), opcional.
- **Usuário**: grade "Balanças do Usuário" (Finalidade + Balança) **apenas na tela de edição** — a
  entidade vive no banco da empresa enquanto o usuário vive no COMMON, então precisa de binding
  próprio (`$$ownRequest`) e do usuário já existente; não cabe deep-insert junto do cadastro.

Nenhuma tela nova ⇒ nenhuma migration de `MENU_ITEMS`.

Armadilhas conhecidas do projeto a respeitar: `targetType: 'any'` nos `Select` de enum (Protocolo,
Finalidade) e nos booleanos; `$$ownRequest` na grade de balanças do usuário; ações OData invocadas com
`(...)` e rota declarada à mão; parâmetro string de ação OData é anulável (`TryGetValue` devolve
`true` com `null`).

## Tratamento de falhas

Cada caso com mensagem própria:

- Balança offline (Client fora do ar ou TCP do indicador caído).
- Peso não estabilizou dentro do tempo limite.
- Captura expirada ou já utilizada → "Capture o peso novamente".
- Peso divergente do comprovante.
- Sem permissão e sem comprovante → "O peso deve ser capturado da balança".
- Caminhão sem tara cadastrada.
- Peso abaixo da tara menos a tolerância, mostrando os dois valores.

## Testes

- `SiagroB1.Application.Tests` (já existe): serviços de 1ª e 2ª pesagem — exigência do comprovante sem
  permissão, comprovante usado / expirado / divergente, administrador digitando, tara ausente, tara
  abaixo do limite, tara dentro da tolerância, gravação de `...ScaleCode` e `...Captured`.
- O parser de frame e o `StabilityDetector` viram classes puras e testadas (frames válidos,
  truncados, com lixo, com sinal negativo). É a parte que será calibrada em campo e a de maior risco.
- Verificação final pelo caminho do usuário, no navegador, com `UseMockScale`. **O mock precisa
  mudar**: hoje ele sobe o peso indefinidamente e nunca estabiliza, então não exercitaria nada da
  captura.

## Fora de escopo

- Leitura serial direta (RS-232) no PC do operador.
- Painel/placar externo de peso, semáforo e cancela.
- Atualização automática da tara do caminhão a partir da pesagem do vazio.
- Refatoração do `MenuService`, que hoje devolve todas as permissões do papel em cada nó do menu.
