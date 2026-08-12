# SiagroB1.Client

Serviço que lê o indicador da balança rodoviária por TCP e transmite o peso ao `SiagroB1.Web` por
WebSocket. Uma instância atende N balanças: uma conexão para cada entrada de `TruckScaleIds`.

Roda como serviço do Windows ou unidade systemd. Não referencia os demais projetos da solução (só
`SiagroB1.Commons`) e é publicado de forma independente.

## Configuração

| Chave | Obrigatória | Descrição |
|---|---|---|
| `TruckScaleIds` | sim | Lista de `TRUCK_SCALES.Code`. Cada código vira uma conexão. |
| `WebSocketUrl` | sim | URL base do endpoint, **sem barra final e sem query**. |
| `UseMockScale` | não (padrão `false`) | `true` gera peso simulado e **ignora o indicador**. Só para desenvolvimento. |
| `TruckScale:ClientKey` | ver abaixo | Chave compartilhada do handshake. |

Exemplo de instalação em campo:

```json
{
  "TruckScaleIds": [ "TS01" ],
  "WebSocketUrl": "ws://servidor:55000/ws/truck-scale",
  "UseMockScale": false,
  "TruckScale": { "ClientKey": "<mesma chave do SiagroB1.Web>" }
}
```

### Para onde a URL aponta

O caminho `/ws/truck-scale` é servido pelo `SiagroB1.Web` (porta 50000), mas o `SiagroB1.Gateway` o
publica pela rota `truck-scale-ws-route`. Então:

- Client na **mesma máquina** do Web → `ws://localhost:50000/ws/truck-scale`.
- Client em **outra máquina** (o caso normal, PC da balança) → a porta pública do Gateway,
  `ws://servidor:5246/ws/truck-scale` no padrão de desenvolvimento.

Apontar para o Gateway **sem** a rota `truck-scale-ws-route` no `appsettings.json` dele devolve 404 no
handshake, e o log do Client mostra apenas `Conexão da balança TS01 caiu.` com
`WebSocketException: ... status code '404' when status code '101' was expected` — o erro não diz que
falta a rota.

### Chave compartilhada

`TruckScale:ClientKey` tem de ser **idêntica** aqui e no `appsettings.json` do `SiagroB1.Web`. O
Client a envia no header `X-Scale-Client-Key`; o Web recusa com 401 quando não bate.

Não configurar a chave libera a conexão, para não quebrar as instalações que seguem só em rede
interna — o Web avisa no boot quando está nesse estado. **Sempre que o caminho estiver publicado pelo
Gateway, configure a chave**: sem ela, quem souber o código de uma balança recebe a configuração do
indicador e injeta peso, e peso injetado vira romaneio.

### Diagnóstico rápido

`curl` comum não é uma requisição WebSocket, então nunca chega a 101 — mas os códigos que ele devolve
já isolam a camada com problema:

```
curl -i "http://servidor:5246/ws/truck-scale"                     # 400 = a rota existe
curl -i "http://servidor:5246/ws/truck-scale?truckScaleId=TS01"   # 401 = chave ausente ou errada
curl -i -H "X-Scale-Client-Key: <chave>" \
        "http://servidor:5246/ws/truck-scale?truckScaleId=TS01"   # 400 = rota e chave OK
```

**404 aqui significa que a rota não existe** — falta `truck-scale-ws-route` no `appsettings.json` do
Gateway, ou ele não foi reiniciado.

Balança não cadastrada **não** aparece no `curl`: o guard de `IsWebSocketRequest` responde 400 antes
de consultar o cadastro, de propósito, para que ninguém sonde quais códigos existem. Esse 404 só sai
num handshake real, e o log do Web o nomeia: `Balança TS01 não cadastrada; conexão recusada.`

Conectado, o log do Client mostra `Balança TS01 conectada ao servidor.` e o do Web,
`Balança TS01 conectada.` Só o primeiro é ambíguo: aparece antes do Web aceitar o socket.

O peso ao vivo (`GET /scales/{code}/live`, SSE) exige usuário autenticado — confira pela tela de
pesagem no navegador, não por `curl`.

## Publicação

⚠️ **O publish sobrescreve o `appsettings.json`.** O arquivo do repositório traz apenas `Logging`, de
propósito: sem `TruckScaleIds`, o serviço morre no boot com
`InvalidOperationException: Configure TruckScaleIds no appsettings.` — falha alta em vez de um serviço
rodando contra o servidor errado.

Escolha um dos dois na instalação:

- preservar o `appsettings.json` no deploy; ou
- passar tudo por variável de ambiente, que tem precedência sobre o arquivo:

```
TruckScaleIds__0=TS01
WebSocketUrl=ws://servidor:55000/ws/truck-scale
UseMockScale=false
TruckScale__ClientKey=<chave>
```

## Calibração do indicador

IP, porta e protocolo **não** ficam aqui: vêm do cadastro da balança, enviados pelo servidor no
`scale_config` ao conectar. Trocar o IP de um indicador não exige acesso à máquina do Client.

Duas consequências práticas:

- a conexão TCP parte **do PC da balança**, então o IP cadastrado precisa ser alcançável dali, não do
  servidor;
- `TruckScale.IpAddress` nulo no cadastro faz o Client tentar `127.0.0.1` e falhar sem explicar.

Para ajustar `WeightLength`, `DecimalPlaces`, `FrameTerminator` e afins, ligue `LogRawFrames` no
cadastro da balança e confira os frames crus no log. Peso parado em zero ou valor absurdo é protocolo
desconfigurado, não falha de conexão.
