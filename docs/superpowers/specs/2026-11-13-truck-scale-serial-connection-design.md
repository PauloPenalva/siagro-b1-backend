# Balança rodoviária: conexão do indicador por TCP/IP ou Serial

Data: 13/11/2026 · Aprovado pelo usuário na conversa (desenho curto).

## Por quê

A CEAGUI (produção, STANDALONE) instalou o `SiagroB1.Client` no PC da balança `BALANCA01`. O Client
conectou ao servidor, mas a balança aparece **offline** na pesagem: o indicador está ligado direto na
porta serial do PC e o Client só sabe ler o indicador por TCP (`ScaleTcpConnection`). O cadastro
(`TRUCK_SCALES`) só tem `IpAddress` e `Port`.

## Objetivo

Cada balança escolhe, no cadastro, **como o Client fala com o indicador**: TCP/IP (como hoje) ou
Serial. As duas opções convivem; uma instalação pode ter balanças dos dois tipos.

## Fora do escopo

- Protocolo novo de indicador. A interpretação do quadro (`ScaleProtocolFactory`, posição fixa ou
  expressão regular) não muda e serve aos dois transportes.
- Controle de fluxo (RTS/CTS, XON/XOFF) e envio de comando ao indicador (modo "pergunta e resposta").
  O Client segue só ouvindo transmissão contínua.
- Descoberta automática de portas COM.

## O que não pode acontecer

- Balança TCP já cadastrada mudar de comportamento. O padrão do campo novo é TCP/IP.
- Peso inventado: falha ao abrir a serial tem de virar "offline", igual à falha de TCP.

## Abordagens consideradas

1. **Tipo de conexão e dados da serial no cadastro (escolhida).** O cadastro já é a fonte única da
   configuração (`ScaleConfigProvider`: "trocar o IP de uma balança não exige acesso à máquina onde
   o Client roda"). Ajustar baud rate ou porta COM continua sendo pela tela.
2. Dados da serial no `appsettings.json` local do Client. Sem migration, mas cria uma segunda fonte
   de configuração e exige acesso ao PC da balança a cada ajuste. Descartada.
3. Ponte serial→TCP de terceiros no PC da balança. Funciona sem mudar o sistema, mas depende de um
   programa externo em cada PC. Fica só como paliativo.

## Desenho

**Cadastro (`TruckScale`)** — campos novos, todos com padrão que preserva o legado:

| Campo | Tipo | Padrão |
|---|---|---|
| `ConnectionType` | enum `ScaleConnectionType { Tcp, Serial }` | `Tcp` |
| `SerialPortName` | `VARCHAR(50)`, nulo | — (`COM3`, `/dev/ttyUSB0`) |
| `BaudRate` | `int?` | nulo = 9600 |
| `DataBits` | `int?` | nulo = 8 |
| `Parity` | enum `ScaleSerialParity { None, Odd, Even }` | `None` |
| `StopBits` | `int?` (1 ou 2) | nulo = 1 |

Migration App `AddTruckScaleSerialConnection`.

**Validação ao salvar** (decisão minha): `Serial` exige `SerialPortName`; `DataBits` só 7 ou 8;
`StopBits` só 1 ou 2; `BaudRate` positivo. Mensagens em pt-BR. O cadastro TCP continua sem
validação nova, para não travar a edição de linhas antigas.

**Servidor → Client** (`ScaleConfigPayload` / `ScaleConfigData`): acrescenta `connectionType`,
`serialPortName`, `baudRate`, `dataBits`, `parity`, `stopBits`, já com os padrões resolvidos. Client
antigo ignora os campos novos e segue tentando TCP — balança Serial exige o Client novo.

**Client:** o laço de leitura (ler bytes → `ScaleFrameBuffer` → protocolo → callback, com
`LogRawFrames`) sai de `ScaleTcpConnection` para uma peça que recebe um `Stream`, testável sem
hardware. Dois transportes abrem o `Stream`: TCP (`TcpClient`) e Serial (`System.IO.Ports.SerialPort`,
`BaseStream`). A reconexão a cada 3 s e o aviso de online/offline ficam iguais nos dois. O
`ScaleWorker` escolhe o transporte pelo `connectionType`.

**Tela do cadastro (`truckScales/fragments/Form.fragment.xml`):** em "Conexão", um seletor "Tipo de
conexão" (TCP/IP · Serial). TCP/IP mostra Endereço IP e Porta; Serial mostra Porta serial, Baud rate,
Bits de dados, Paridade e Bits de parada.

**Instalador do Client:** nada muda na tela; o pacote passa a levar a biblioteca de serial.

## Deploy

Migration e backend antes do frontend (a tela nova faz PATCH de campos que o backend antigo não
conhece). Depois, reinstalar o Client no PC da balança e reiniciar o serviço: a configuração só é
enviada na conexão.

## Verificação

- Testes automatizados: laço de leitura sobre `Stream` (quadros partidos, fim do fluxo, cancelamento),
  resolução de padrões no `ScaleConfigProvider`, validação do cadastro.
- Ponta a ponta local sem hardware: par de portas seriais virtuais ou transporte TCP contra um
  emissor de quadros, com Web + Gateway + Client, conferindo o peso ao vivo na tela.
- Em produção: `LogRawFrames` ligado na primeira conexão para conferir o quadro do indicador real.

## Em aberto

Marca e modelo do indicador da CEAGUI e os parâmetros da serial. Não bloqueia a implementação: são
valores de cadastro.
