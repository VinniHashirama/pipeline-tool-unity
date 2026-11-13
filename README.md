# 🐸 Hopper — Import Client

Unity Editor window para listar e importar assets 3D aprovados direto do Hopper.

- **Package name:** `com.antigravity.pipeline-tool` *(o identificador mantém o nome antigo
  de propósito — trocá-lo quebraria o `manifest.json` de projetos já instalados)*
- **Repositório:** https://github.com/VinniHashirama/pipeline-tool-unity
- **Unity mínimo:** 2021.3 LTS

---

## Instalação

### Via Package Manager UI (recomendado)

Abra **Window > Package Manager**, clique no botão **+** no canto superior esquerdo e escolha uma das opções abaixo.

#### Opção 1 — Git URL (direto do GitHub)

Escolha **"Add package from git URL…"** e cole:

```
https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.9.0
```

Para fixar em uma versão específica, adicione `#v<semver>` ao final. Omita a tag para usar sempre o latest `main` (apenas em dev).

#### Opção 2 — Pasta local (desenvolvimento ativo)

Escolha **"Add package from disk…"** e navegue até o `package.json` dentro da pasta `unity-package/` clonada localmente.

### Via `Packages/manifest.json`

```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.9.0"
  }
}
```

Para desenvolvimento local:

```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "file:C:/caminho/absoluto/para/unity-package"
  }
}
```

---

## Configuração

Abra **Hopper > Import Window** na barra de menus do Unity.

Na primeira abertura, o tool exibe a **tela de login** (e-mail e senha da conta do Hopper). O servidor é
sempre **Production** — não há o que escolher.

### Onde cada configuração fica (v0.8)

| Configuração | Onde | Quem vê |
|---|---|---|
| **Hopper Project** e **Target Folder** | `ProjectSettings/HopperSettings.json` — **commitar** | O time todo: quem clona o repo do jogo já vem no projeto e na pasta certos |
| Login (tokens), usuário, cache de permissões | `EditorPrefs` | Só esta máquina. Nunca no repositório |
| Auto-refresh sync on start | `EditorPrefs` | Só esta máquina |
| Servidor de dev (Local/Custom) | `EditorPrefs` | Só aparece com o plugin instalado por `file:` (teste interno) |

O arquivo compartilhado não guarda nada pessoal nem segredo: só id e nome do projeto e a pasta.

### Aba Settings (após login)

- **This Unity project · shared with the team (Git)** — **Hopper Project** (trocar pede confirmação, porque
  muda para todo mundo) e **Target Folder** (**Save**). Depois de mudar, commite o `HopperSettings.json`.
- **This machine only** — servidor (só leitura, ou o override de dev numa instalação local) e auto-refresh.

Se o projeto ligado não está entre os da sua conta, a janela avisa ("your account cannot access") e não troca
nada: peça acesso no Hopper. Quem vem da v0.7 vê um aviso para reaproveitar o projeto e a pasta que a máquina
guardava — vale conferir, porque o `EditorPrefs` é da máquina e pode ser de outro jogo.

---

## Uso

O passo a passo para quem usa (Import, Update, Move, Missing, Reimport, Forget e o overlay
**Hopper Sync**) está na aba **Docs** do Hopper: https://pipeline-tool-web.vercel.app/dashboard/docs#unity

Em resumo: **Refresh** lista os Itens aprovados do projeto; **Import** traz o Item para
`[Target Folder]/Tipo/Categoria/Item/` e registra o commit na versão; **Update** troca pela versão
publicada mais nova no mesmo caminho, preservando o GUID.

### Mapa de blendshapes (v0.9)

Quando o rig e a animação não concordam no nome de um blendshape (o SK_ do Billie tem a piscada em
`Expressions.blinkb`, a animação anima `Expressions.blink`), o mapa de blendshapes do arquivo no Hopper diz para
onde cada curva vai — ou que ela sai do clipe. O Import aplica sozinho, e o mapa fica no `userData` do importer
(no `.meta`), então um reimport manual continua valendo. Editar o mapa no Hopper faz o arquivo aparecer como
**Update**, que reaplica sem baixar. Precisa de `#v0.9.0` no `Packages/manifest.json` do jogo; versões anteriores
ignoram o mapa.

---

## Autenticação

O tool usa **JWT Supabase** (email + senha) — as mesmas credenciais da web app.

**Fluxo:**
1. Usuário informa email + senha na tela de login
2. Tool chama `POST /api/auth/login` no servidor configurado
3. Servidor retorna `access_token` + `refresh_token`
4. Tokens são armazenados no `EditorPrefs` (plain text — limitação conhecida do Editor)
5. O token é renovado automaticamente antes de cada chamada quando está a menos de 60s do vencimento
6. Em caso de 401 (sessão expirada), o tool limpa a sessão e volta ao login

**Legacy:** existe suporte a `X-Pipeline-Key` (API key) para uso em CI/automação, mas não aparece na UI.

---

## Versionamento

```bash
# Edite "version" em package.json, depois:
git add package.json package.json.meta
git commit -m "chore: bump version to 0.9.0"
git push
git tag v0.9.0
git push origin v0.9.0
```

No Unity, atualize `manifest.json` para a tag nova (ex.: `#v0.9.0`) — o Package Manager baixa a nova versão automaticamente.

A cada tag, atualize também `docs/plugins.json` e `docs/novidades.md` no repositório web (aba Docs) e as linhas de instalação deste README.

---

## Estrutura do package

```
com.antigravity.pipeline-tool/
├── package.json
├── README.md
└── Editor/
    ├── PipelineTool.Editor.asmdef
    └── Scripts/
        ├── Models/
        │   ├── ApprovedAsset.cs     — response shape de GET /api/assets/approved
        │   ├── PublishedItem.cs     — feed por Item (animation, clips, shape_map)
        │   ├── AuthModels.cs        — LoginRequest, AuthResponse, ProjectInfo e afins
        │   └── ImportResult.cs      — response shape de PATCH /mark-imported
        ├── PipelineSettings.cs      — EditorPrefs: sessão, permissões, override de dev; repassa projeto e pasta
        ├── HopperProjectSettings.cs — ProjectSettings/HopperSettings.json: projeto e pasta, no Git
        ├── PipelineApiClient.cs     — chamadas HTTP assíncronas (login, refresh, projetos, assets)
        ├── ShapeMap.cs              — mapa de blendshapes: userData do importer + AssetPostprocessor (v0.9)
        ├── AssetDownloader.cs       — download via proxy + AssetDatabase refresh
        └── PipelineImportWindow.cs  — EditorWindow: login, aba Assets, aba Settings
```
