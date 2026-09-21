# CLAUDE.md — Pipeline Tool Unity Package

Unity UPM package providing an Editor window to import approved 3D assets from Pipeline Tool.
Part of a two-repo workspace — see the workspace root `CLAUDE.md` for the cross-repo architecture and API contract.

## Package identity

```
name:    com.antigravity.pipeline-tool
version: 0.1.0
unity:   2021.3+
repo:    https://github.com/VinniHashirama/pipeline-tool-unity
```

## Structure

```
Editor/
├── PipelineTool.Editor.asmdef      — Editor-only assembly (includePlatforms: Editor)
└── Scripts/
    ├── Models/
    │   ├── ApprovedAsset.cs        — mirrors GET /api/assets/approved response (inclui AssetCategory)
    │   ├── ImportResult.cs         — mirrors PATCH /mark-imported response
    │   └── AuthModels.cs           — LoginRequest, AuthResponse, AuthUser, ProjectInfo, ProjectsResponse, UserPermissions
    ├── PipelineSettings.cs         — EditorPrefs wrapper (API URL, session tokens, project, import path, auto-refresh sync flag, permissões)
    ├── PipelineApiClient.cs        — HTTP client: Login, Refresh, GetUserProjects, GetPermissions, GetApprovedAssets, GetProjectAssetsForSync, MarkImported
    ├── AssetDownloader.cs          — download via proxy + hierarquia de pastas local (TypePlural/Category?/AssetTitle/) + registra entrada no manifesto
    ├── PipelineManifest.cs         — lê/grava .pipeline-manifest.json (chaveado por GUID) — mapeia asset local → asset_id/version_id/hash
    ├── PipelineSyncStatus.cs       — overlay de ícone de sync na aba Project (projectWindowItemOnGUI) + refresh contra o servidor
    └── PipelineImportWindow.cs     — EditorWindow: tela de login + Assets tab (Refresh / Sync Status) + Settings tab
```

Everything is Editor-only — no Runtime assembly. The package has no dependency on other UPM packages.

## Dev Workflow — LEIA ANTES DE EDITAR

**Nunca desenvolva com o package instalado via Git URL.** A pasta de cache do UPM é imutável — o Unity não consegue gerar `.meta` files lá, e o resultado é:
```
Asset has no meta file, but it's in an immutable folder. The asset will be ignored.
```

O ciclo correto:

```
1. Editar arquivos (VS Code, Rider, etc.)
2. Abrir o projeto Dev no Unity (instalado via file: local)
3. Unity detecta mudanças, compila automaticamente
4. Ao criar arquivos novos → Unity gera os .meta correspondentes
5. Testar na Engine
6. git add . && git commit   ← inclui os .meta novos
7. git push
8. Se for release: bump version em package.json, depois:
   git tag v0.x.0 && git push origin v0.x.0
```

### Por que os `.meta` files precisam estar no repo

O Unity usa os GUIDs dentro dos `.meta` para referenciar assets internamente. Sem eles no repo, um Git URL install é quebrado. O `.gitignore` deste repo **não ignora** `.meta` — isso é intencional e não deve ser mudado.

### Estrutura de projetos recomendada

```
d:\Projects\
├── PipelineTool\               ← workspace (workspace repo)
│   ├── unity-package\          ← este repo (file: aponta aqui)
│   └── pipeline-tool\          ← repo web
└── PipelineTool-Unity-Dev\     ← projeto Unity Dev (fora do workspace, não vai pro Git)
    └── Packages\
        └── manifest.json       ← "com.antigravity.pipeline-tool": "file:..."
```

O projeto Dev não precisa ir pro Git — é uma sandbox local para validar antes de taggear.

### Criar novos arquivos

```bash
# 1. Crie o arquivo no editor de código
# 2. Abra o projeto Dev no Unity → .meta é gerado automaticamente
git status   # confirme que NomeDoArquivo.cs.meta aparece como untracked
git add NomeDoArquivo.cs NomeDoArquivo.cs.meta
git commit -m "feat: ..."
```

Nunca commite um `.cs` (ou qualquer asset) sem o `.meta` correspondente.

### Gotcha: Rider IDE gera `.approj`

Ao abrir um projeto Unity que referencia este package via `file:`, o Rider cria um arquivo `unity-package.approj` dentro da pasta do package. Este arquivo já está no `.gitignore` (`*.approj` e `*.approj.meta`). Não remova essas entradas do `.gitignore`.

## Instalação

### Package Manager UI

`Window > Package Manager` → botão `+` → escolha:

- **Add package from git URL** → `https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0`
- **Add package from disk** → navegue até o `package.json` da pasta local

### `Packages/manifest.json`

```json
// Pinado em versão (QA / produção)
"com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0"

// Latest main (só em dev, sem pin)
"com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git"

// Caminho local (desenvolvimento ativo)
"com.antigravity.pipeline-tool": "file:C:/caminho/absoluto/para/unity-package"
```

## Configuração

Abra **Pipeline Tool > Import Window**. Na primeira abertura, o tool exibe uma tela de login.

### Tela de Login

| Campo | Descrição |
|---|---|
| Server | Dropdown: **Local** (localhost:3000) · **Production** (URL Vercel) · **Custom** (campo livre) |
| Email | Email da conta no Pipeline Tool (mesma usada na web app) |
| Password | Senha da conta |

O picker de servidor aparece **antes** do login — permite escolher o ambiente sem precisar logar primeiro.
Após login, o tool busca automaticamente os projetos do usuário e exibe um dropdown na aba Settings.

### Aba Settings (após login)

| Campo | Descrição |
|---|---|
| Signed in as | Nome do usuário logado + botão **Sign Out** |
| Server | Mesmo picker da tela de login — alterável também após logar |
| Project | Dropdown com os projetos do usuário — salva imediatamente ao selecionar |
| Target Folder | Pasta destino dentro do projeto Unity, ex: `Assets/ImportedAssets` |

**Para atualizar a URL de Production:** edite a constante `ProductionApiUrl` em `PipelineSettings.cs` com a URL real da Vercel após o primeiro deploy.

Settings ficam no `EditorPrefs` — por usuário, por máquina. Nunca commitados.

## Autenticação

O tool usa **Supabase JWT** (email + password) — as mesmas credenciais da web app.

**Fluxo:**
1. Usuário informa email + senha na tela de login
2. Unity chama `POST /api/auth/login` no servidor web (proxy para Supabase Auth)
3. Servidor retorna `access_token` (JWT, expira em 1h) + `refresh_token`
4. Tokens armazenados no `EditorPrefs` (plain text — limitação conhecida do Editor)
5. Antes de cada chamada à API, o client verifica a expiração e chama `POST /api/auth/refresh` automaticamente
6. Em caso de 401 (sessão totalmente expirada), o tool limpa a sessão e volta à tela de login

Todos os requests incluem `Authorization: Bearer <access_token>`. O servidor usa `createBearerClient(token)` para autenticar sem cookie.

**Legacy:** o campo `Pipeline API Key` (X-Pipeline-Key) ainda existe no `PipelineSettings` para uso em automações/CI, mas não aparece na UI da janela.

## Download de Assets

O tool **nunca acessa diretamente** o Supabase Storage ou o Google Drive. O download é feito via:

```
GET /api/assets/{id}/download?version_id={vid}
Authorization: Bearer <access_token>
```

O servidor busca o arquivo no storage correto (Supabase ou GDrive) e entrega como stream. Isso resolve dois problemas:
- **Supabase Storage:** bucket privado — URLs públicas não funcionam sem auth
- **Google Drive:** `file_url` no banco é um path relativo (`/api/gdrive/file/{id}`), sem hostname

## Hierarquia de pastas local

O `AssetDownloader` cria automaticamente a hierarquia de pastas no projeto Unity espelhando a estrutura do Pipeline Tool:

```
[Target Folder]/
  Props/
    Industrial/          ← categoria (se definida)
      PROP_Conteiner/    ← título da task
        PROP_Conteiner_v3.fbx
  Characters/
    CH_Guerreiro/
      CH_Guerreiro_v1.fbx
```

**Mapeamento de tipos:**
`prop → Props` · `character → Characters` · `environment → Environments` · `vfx → VFX` · `ui → UI` · `audio → Audio` · `other → Other`

Assets sem categoria vão direto sob o tipo: `Props/PROP_Cadeira/file.fbx`.

O **Target Folder** é configurável na aba Settings (default: `Assets/ImportedAssets`). O nome do projeto **não** faz parte do path local — a organização por projeto fica por conta do Target Folder escolhido pelo tech artist.

Os modelos `ApprovedAsset` e `AssetCategory` em `Models/ApprovedAsset.cs` espelham o response do servidor (campo `category` é nullable).

## Sync Status Overlay

Depois de importar um asset, o tool grava uma entrada em `.pipeline-manifest.json` (na raiz do **Target Folder**) associando o asset local à sua origem no Pipeline Tool. A aba Project passa a mostrar um ícone sobre cada asset rastreado:

| Ícone | Estado | Significado |
|---|---|---|
| `✓` verde | Synced | O arquivo local bate com a versão mais recente aprovada/importada no servidor |
| `!` laranja | Outdated | Existe uma versão mais nova no servidor do que a baixada localmente |
| `M` azul | Modified Locally | O conteúdo do arquivo local mudou desde o download (hash diverge) — provável edição manual fora do pipeline |

### Formato do manifesto

`.pipeline-manifest.json` é um arquivo único, **commitado no repositório do jogo** (visibilidade compartilhada entre o time), chaveado pelo **GUID** do asset — não pelo path. O GUID é gerido pelo próprio Unity via `.meta`; o tool só o lê via `AssetDatabase`, nunca escreve nele, então não há risco de mexer nas configurações de import do asset. Isso também significa que renomear/mover um asset rastreado no Unity não quebra a referência.

```json
{
  "entries": [
    { "guid": "a1b2c3...", "asset_id": "uuid", "version_id": "uuid", "version_number": 3, "task_title": "PROP_Conteiner", "local_hash": "sha1..." }
  ]
}
```

Como é um arquivo único compartilhado, existe risco residual de conflito de merge quando duas pessoas importam/atualizam assets diferentes em paralelo — mitigado mantendo as entradas sempre ordenadas por GUID antes de salvar (ajuda o merge automático do git). Na prática é um conflito de JSON simples de resolver manualmente. Se isso virar recorrente, um merge driver customizado é a próxima etapa — não implementado agora.

### Quando o refresh de sync acontece

Nunca dentro do callback de desenho (`projectWindowItemOnGUI`, que roda a cada repaint da aba Project) — isso só lê os dicionários já calculados em memória. A chamada de rede (`PipelineSyncStatus.RefreshAsync`) dispara em dois lugares:

- Automaticamente quando a Import Window é aberta/reabilitada (`OnEnable`), se `PipelineSettings.AutoRefreshSyncOnStartup` estiver ligado (default: `true`).
- Manualmente pelo botão **Sync Status** na aba Assets.

O toggle **Auto-refresh on Editor start**, na aba Settings, desliga o gatilho automático — útil em projetos grandes onde o custo do refresh (ler hash de cada asset rastreado do disco) pode incomodar.

## Releases

Sempre bump `version` em `package.json` antes de taggear. Use `v<semver>`.

```bash
# Edite package.json: "version": "0.2.0"
git add package.json package.json.meta
git commit -m "chore: bump version to 0.2.0"
git push
git tag v0.2.0
git push origin v0.2.0
```

## Permissões

Depois do login (e ao reabrir a janela com sessão restaurada), o tool chama:

```
GET /api/user/permissions?project_id=<uuid>
Authorization: Bearer <access_token>
```

A resposta é achatada de propósito — o `JsonUtility` não desserializa `Dictionary` nem array na raiz, então a lista vem como `string[]` dentro do objeto:

```json
{
  "allowed": ["asset.download", "unity.import", "..."],
  "is_admin": false,
  "role": "tech_artist",
  "enforcement_enabled": false,
  "matrix_version": 3
}
```

As permissões ficam no `EditorPrefs` (`PipelineTool.Permissions`, separadas por vírgula) e são consultadas por `PipelineSettings.Can("unity.import")`. Como são **por projeto**, trocar o projeto no dropdown dispara um novo fetch.

O botão **Import** fica desabilitado sem `unity.import`, e `ImportAsync` também corta na entrada. Isso é só conveniência: quem decide é o servidor, em `PATCH /api/assets/[id]/mark-imported`, que responde 403.

**Admin vê todos os projetos** — `GET /api/user/projects` devolve o sistema inteiro quando o chamador tem `is_admin`, sem precisar se adicionar como membro de cada projeto. O Unity não precisou mudar para isso.

### Gotcha: "ainda não carregou" ≠ "não pode nada"

`PipelineSettings.Can()` devolve `true` enquanto as permissões não chegaram — sessão recém-restaurada, rede fora — para que a janela não fique inutilizável por falta de dado. Por isso existe a flag separada `PipelineTool.PermissionsLoaded`: sem ela, um usuário que realmente não tem permissão nenhuma (lista vazia) seria confundido com "ainda não carregou" e veria tudo habilitado.

`ClearSession()` limpa as três chaves (`Permissions`, `PermissionsLoaded`, `IsAdmin`) junto com os tokens.
