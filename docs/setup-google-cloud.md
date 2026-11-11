# Configurar o Google Cloud (uma vez só)

O app precisa de um **OAuth Client** do Google para fazer login e ler as agendas.
Tudo aqui é gratuito. Leva uns 10 minutos.

> A interface do console muda de tempos em tempos. Se um nome de menu estiver diferente,
> procure pelo equivalente: hoje a parte de OAuth fica em **Google Auth Platform**.

## 1. Criar o projeto

1. Acesse <https://console.cloud.google.com/> com a sua conta Google.
2. No seletor de projetos (topo), **Novo projeto** → nome `juntos-agenda` → Criar.
3. Confirme que o projeto novo está selecionado no topo.

## 2. Ativar as APIs

Em **APIs e serviços → Biblioteca**, procure e clique em **Ativar** em:

- **Google Calendar API**
- **Google Tasks API**

## 3. Tela de consentimento (Google Auth Platform)

Menu **Google Auth Platform** → **Primeiros passos** (Get started):

| Campo | Valor |
|---|---|
| Nome do app | `Juntos` (é o que aparece na tela de permissão) |
| E-mail de suporte | o seu |
| Público (Audience) | **Externo** (External) |
| Contato | o seu e-mail |

Depois, em **Acesso a dados** (Data Access) → **Adicionar ou remover escopos**, adicione:

- `https://www.googleapis.com/auth/calendar.readonly`
- `https://www.googleapis.com/auth/tasks.readonly`

(`openid`, `email` e `profile` já vêm por padrão.)

## 4. Usuários de teste e publicação

**Agora (dev):** em **Público-alvo** → **Usuários de teste** → **Add users**, adicione os dois e-mails
(o seu e o dela). Isso já basta para o app funcionar enquanto está em *Testando*.

**Publicar:** o botão **Publicar app** só fica ativo quando a página **Branding** está completa
(nome do app, e-mail de suporte, logo opcional e, em **Domínio do app**, links de página inicial
e política de privacidade + **Domínios autorizados**). Domínio autorizado não aceita `localhost`,
então na prática a publicação fica para o **M4**, quando tivermos domínio próprio e as páginas
`/privacidade` e `/termos`.

> Se o botão continuar desativado com só os campos obrigatórios (*) preenchidos, siga em *Testando*.
> O custo é o que está abaixo.

**Por que publicar importa:** enquanto o app está em *Testando*, o Google faz o **refresh token expirar em 7 dias**.
Em dev isso é aceitável: quando expirar, o app mostra a tela "Sua conexão com o Google expirou" e basta
clicar em **Conectar Google Agenda**. Em uso real seria ruim: se um de vocês não reconectar, a agenda dele
some para o outro.

Em produção sem verificação:

- aparece a tela **"O Google não verificou este app"**. Clique em **Avançado → Acessar Juntos**.
  É esperado: o app é de vocês;
- o limite é de **100 usuários**, mais que suficiente agora;
- a verificação oficial (gratuita, exige domínio próprio + política de privacidade + vídeo)
  só será necessária ao abrir para o público.

## 5. Criar o OAuth Client

**Google Auth Platform → Clientes → Criar cliente**:

| Campo | Valor |
|---|---|
| Tipo | **Aplicativo da Web** |
| Nome | `Juntos (dev)` |
| URIs de redirecionamento autorizados | `http://localhost:5173/signin-google` |

> **Copie o Client Secret na hora** (ou baixe o JSON). O console novo só mostra o secret
> no momento da criação. Se perder, é preciso gerar outro.

Por que `5173`? Em dev o navegador fala com o Vite, que repassa `/signin-google` para a API.
O ASP.NET monta o `redirect_uri` a partir do endereço que o navegador usou.

Quando publicarmos (M4), vamos adicionar aqui a URL de produção, ex.: `https://seu-dominio/signin-google`.

## 6. Guardar as credenciais (fora do código)

Na raiz do repositório:

```bash
dotnet user-secrets set "Google:ClientId" "SEU_CLIENT_ID.apps.googleusercontent.com" --project src/Agenda.Api
```

```bash
dotnet user-secrets set "Google:ClientSecret" "SEU_CLIENT_SECRET" --project src/Agenda.Api
```

O **user-secrets** guarda os valores num arquivo do seu perfil do Windows
(`%APPDATA%\Microsoft\UserSecrets\`), fora da pasta do projeto, então nunca vão parar no Git.
Em produção o mesmo papel será do **Azure Key Vault**.

## 7. Testar

Siga o "Como rodar" do [README](../README.md) e faça login com as duas contas (a sua e a dela).

## Problemas comuns

| Erro | Causa provável |
|---|---|
| `redirect_uri_mismatch` | A URI cadastrada não é exatamente `http://localhost:5173/signin-google` (atenção a `https`, porta e barra no fim). |
| `access_denied` / app bloqueado | O app ainda está em *Teste* e a conta não está em "Usuários de teste". Publique (passo 4). |
| A API não sobe: "Google:ClientId e Google:ClientSecret não configurados" | Faltou o passo 6. |
| Tela "Falta conectar sua agenda" depois do login | Você desmarcou a permissão da agenda na tela do Google. Clique em "Conectar Google Agenda" e deixe marcada. |
