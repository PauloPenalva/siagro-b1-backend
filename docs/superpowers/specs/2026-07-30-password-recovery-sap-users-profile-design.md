# Recuperação de senha, integração OUSR (SAP B1) e perfil pessoal

Data: 30/07/2026

## Contexto

O SiagroB1 não tinha nenhum caminho para trocar senha: `UsersUpdateService` ignorava `PasswordHash`
de propósito, e a única forma de definir uma senha era na criação do usuário. Quem esquecia a
senha dependia de alteração manual no banco.

Três problemas de fundo apareceram junto e precisavam ser resolvidos na mesma entrega, porque um
mecanismo de redefinição só faz sentido se o segredo estiver protegido:

- o hash era **SHA-256 sem salt**;
- `USERS.Password` era uma coluna **de texto puro persistida** e exposta em `GET /odata/Users`,
  legível por qualquer usuário autenticado;
- em `Erp=SAPB1` o cliente quer manter o cadastro de usuários no SAP B1 (tabela `OUSR`), e não
  havia nenhuma ligação entre os dois cadastros.

## Decisões

| Tema | Decisão |
|---|---|
| Canal de recuperação | E-mail por SMTP (MailKit), infra criada do zero |
| Formato | Link com token, 30 min, uso único, derruba as sessões ativas |
| Sync OUSR | Espelho automático: `USER_CODE`→`Username`, `U_NAME`→`FullName`, `E_Mail`→`Email` |
| Usuário sem par no SAP | Desativa, **nunca** apaga |
| 1º acesso de usuário do SAP | Pelo próprio "esqueci minha senha" |
| Hash | PBKDF2 com migração transparente; coluna de texto puro removida |
| Meu Perfil | Foto, tema e troca de senha; nome/usuário/e-mail somente leitura |
| Foto e tema | Persistidos em `USERS` (acompanham o usuário em qualquer máquina) |

## Arquitetura

### Senha

`SiagroB1.Security\Shared\PasswordHasher.cs` grava `PBKDF2$<iterações>$<salt>$<hash>`
(PBKDF2-SHA256, 210.000 iterações, salt de 16 bytes; sem pacote novo). `Verify` aceita também o
formato antigo e devolve `needsUpgrade`, e os dois pontos de login (`AuthService.LoginAsync` e
`BasicAuthenticationHandler.ValidateUserAsync`) regravam o hash na primeira autenticação. Toda a
base migra sozinha, sem ninguém perder o acesso.

`User.Password` virou `[NotMapped]` — sai do banco mas continua no EDM do OData (é atributo do EF,
que o `ODataConventionModelBuilder` não enxerga), então a tela de criação segue funcionando.
`PasswordHash`, `PhotoContent` e `PhotoContentType` são ignorados no EDM.

### Política de senha

`PasswordPolicy` acompanha o padrão do SAP Business One, onde as senhas em uso são curtas e só de
dígitos: exigir mais aqui do que o SAP exige lá deixaria parte dos usuários sem conseguir repetir
a senha que já usa. Decisão do cliente, registrada de propósito — a proteção real passa a ser o
PBKDF2, o uso único do token e a invalidação de sessões, não a força da senha.

Configurável por ambiente, sem tocar em código:

```json
"Security": { "PasswordPolicy": { "MinimumLength": 4, "RequireLetterAndDigit": false } }
```

Há um piso absoluto de 1 caractere: hash nulo/vazio é o que marca "usuário ainda sem senha" (o
que vem do sync do OUSR), e senha vazia apagaria essa distinção.

O texto da regra vem do servidor (`GET reset-password/validate` e `GET me/profile`) e as telas o
exibem: um aviso fixo no XML viraria mentira assim que a configuração mudasse.

### Recuperação

`IEmailSender`/`SmtpEmailSender` (MailKit). Com `Email:Enabled = false` — padrão em
desenvolvimento — nada é enviado e o e-mail vai para o log, o que permite exercitar o fluxo
inteiro sem servidor SMTP.

`PASSWORD_RESET_TOKENS` guarda apenas o **hash** do token. `PasswordResetService`:

- `RequestAsync` — responde sempre igual, exista ou não a conta (senão o endpoint público viraria
  um verificador de usuários válidos); throttle de 3 pedidos por 15 min; em SAPB1 provisiona do
  OUSR antes de procurar o usuário;
- `ResetAsync` — valida a senha **depois** de achar o token e **antes** de consumi-lo (senha fraca
  não pode queimar o link); marca o token e todos os pendentes do usuário como usados; desativa
  todas as sessões ativas dele.

Endpoints anônimos no Gateway: `POST security/auth/forgot-password`,
`GET security/auth/reset-password/validate`, `POST security/auth/reset-password`.

### OUSR

`SapUser` (`OUSR`) no `SapErpDbContext`. `SapUserMapper` concentra as regras, usado pelos dois
caminhos que sincronizam:

- **provisionamento pontual** (`ISapUserProvisioner`), no login e no pedido de recuperação, lendo
  uma linha só e engolindo qualquer falha — o SAP fora do ar não pode derrubar o login;
  `NullSapUserProvisioner` cobre o modo standalone, então o código de autenticação nunca precisa
  saber em que modo está rodando;
- **varredura completa** (`SapUserSyncService` + job Hangfire `sap-user-sync`, a cada 15 min, só
  em SAPB1), que é o único caminho capaz de enxergar quem sumiu do OUSR e desativá-lo. Também
  exposta sob demanda pela action OData `UsersSyncFromSap` (somente admin).

Nunca são tocados `PasswordHash`, `IsAdmin`, tema, foto e perfis: do SAP vêm só identificação e
situação. Ninguém é apagado. `SapUserSync:ProtectedUsernames` (padrão `["admin"]`) impede que a
varredura desative a conta local de administração e tranque todo mundo para fora.

### Perfil

Colunas novas em `USERS`: `Theme`, `PhotoContent`, `PhotoContentType`. `UserProfileService` e os
endpoints `security/users/me/*` resolvem o usuário **pelas claims da sessão**, nunca por um
identificador vindo da requisição. A foto tem endpoint próprio (`GET me/photo`, 204 quando não
existe) — o blob nunca trafega junto do perfil nem do `/status`.

No frontend, `SessionService` publica `fullName`, iniciais, tema e URL da foto no `sessionModel`;
o `Avatar` da shell e o título do popover passam a ser ligados a esses valores. O tema é aplicado
com `Theming.setTheme`, com o banco como fonte da verdade e um espelho em `localStorage` só para
evitar o piscar de tema no boot.

## Armadilhas encontradas (só apareceram contra o ambiente real)

1. **`OUSR.USERID` é `smallint`.** Mapeado como `int`, a leitura estoura com
   *"Unable to cast object of type 'System.Int16' to type 'System.Int32'"*.
2. **`USERS.Username` e `USERS.Email` usam collation `Latin1_General_100_CI_AI`** — ignora
   maiúsculas **e acentos**. Comparar em C# com `OrdinalIgnoreCase` trata "João" e "Joao" como
   nomes diferentes; o SQL Server trata como o mesmo. O OUSR do cliente tem exatamente esse par, e
   a gravação abortava a sincronização inteira. `SapUserMapper.NormalizeKey` reproduz a semântica
   da collation, e o segundo usuário é ignorado com log em vez de derrubar tudo.
3. **O 401 do OData expulsava quem estava numa rota pública.** Nas telas anônimas o 401 é o estado
   normal; o handler global de mensagem redirecionava para o login e tirava da tela justamente
   quem chegou pelo link do e-mail.
4. **`BaseController.setBusy` estourava no primeiro `patternMatched` de uma rota**, porque a view
   ainda não recebeu os modelos do Component.
5. **O link de reset precisa apontar para `/index.html#/...`**, não para `/#/...`: o servidor de
   desenvolvimento do UI5 responde a raiz com a listagem do diretório.

## Verificação

Verificado no browser, com backend `yktb` (SAPB1) e SAP real:

- login com hash legado migra para PBKDF2 no primeiro acesso; `admin/1234` continua entrando;
- `GET /odata/Users` não expõe mais `Password` nem `PasswordHash`;
- "esqueci minha senha" → link no log → tela valida o token → senha fraca recusada sem queimar o
  link → senha válida aceita → login com a nova senha; token reusado e validação posterior negados;
- avatar mostra iniciais reais e o popover o nome completo; foto sobe e aparece na shell sem F5;
- tema aplica na hora, persiste no banco e volta mesmo com o `localStorage` limpo;
- troca de senha recusa senha atual incorreta;
- sincronização com o SAP: 174 criados, 1 ignorado (colisão de acento), 1 desativado (sem par no
  OUSR), `admin` intacto, 59 e-mails descartados por vazio/duplicado;
- usuário criado pelo sync, sem senha, define a primeira senha pelo "esqueci minha senha".

747 testes de backend passando. `ts-typecheck` e `eslint` limpos.
