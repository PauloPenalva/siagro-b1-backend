# Regra de senha visível na criação de usuário

**Data:** 08/08/2026

## Problema

A tela de novo usuário é o único dos três caminhos de senha que não mostra a regra vigente. O
administrador digita, salva, e só então descobre o mínimo pela mensagem de erro. As telas de
redefinição por e-mail e de "Meu Perfil" já exibem a regra.

Isso ficou aparente ao ligar a `PasswordPolicy` no `UsersCreateService` — antes a criação aceitava
qualquer senha, então não havia regra a comunicar.

## Padrão existente (é dele que o desenho sai)

| Tela | De onde vem o texto |
|---|---|
| Redefinição por e-mail | `GET /security/auth/reset-password/validate` → `passwordRequirements` |
| Meu Perfil | `GET /security/users/me/profile` → `passwordRequirements` |

Duas propriedades desse padrão que o desenho preserva:

1. **O texto vem pronto do servidor** (`PasswordPolicy.Description`), pendurado num endpoint que a
   tela já chamava. Não existe endpoint dedicado a política, e não se repete a regra em XML — ela é
   configurável por ambiente e um texto fixo divergiria dela na primeira mudança.
2. **Nenhuma das telas valida a regra no cliente.** O `ResetPassword` confere apenas "preenchida" e
   "confere com a confirmação"; quem reprova senha fraca é o servidor. Exibir sem validar evita
   duplicar a regra em TypeScript.

## Desenho

### Backend

`GET /security/auth/status` passa a devolver `PasswordRequirements`, **nos dois ramos** (identidade
reconstruída do cookie e identidade vinda do principal). O `AuthController` recebe a
`PasswordPolicy` injetada — já registrada como singleton no `Gateway/Program.cs`.

O `/status` é o endpoint certo pelo mesmo motivo que levou `Permissions` para lá: depois do boot
ele é a única fonte de identidade, e o login só acontece uma vez por sessão. Um dado publicado só
no login se perde em todo F5.

Nenhum endpoint novo. Nenhuma mudança de comportamento no servidor — a validação já existe em
`UsersCreateService`.

### Frontend

- `AuthStatus` (em `types/UserIdentity.ts`) ganha `passwordRequirements`. O campo já existe tipado
  no arquivo, hoje só no `UserProfile`.
- `SessionService.applyUserIdentity` grava em `sessionModel>/passwordRequirements`, junto das
  demais propriedades de identidade.
- `CreateUserForm.fragment.xml` exibe um `<Text>` abaixo do campo Senha, como o `Main.view.xml` do
  perfil.

Efeito colateral desejado: uma vez no `sessionModel`, qualquer tela futura que peça senha mostra a
regra sem ida extra ao servidor.

## Fora de escopo

- Validação da regra no cliente.
- A obrigatoriedade do campo Senha na tela (`required="true"`), embora o servidor aceite criar
  usuário sem senha. É uma divergência real, mas anterior e independente.
- Harness de teste para controllers do Gateway (ver abaixo).

## Testes

O valor devolvido é `passwordPolicy.Description`, já coberto por `PasswordPolicyTests`.

Os controllers do Gateway **não têm harness de teste** — o projeto de testes não referencia o
Gateway. Fazer TDD das duas linhas do `AuthController` exigiria construir esse harness primeiro,
tarefa maior que a própria feature. Decisão consciente: a ligação `/status` → `sessionModel` → tela
é verificada no navegador, que é onde esse tipo de fio arrebenta neste projeto.
