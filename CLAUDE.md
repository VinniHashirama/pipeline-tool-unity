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
    │   ├── ApprovedAsset.cs        — mirrors GET /api/assets/approved response
    │   ├── ImportResult.cs         — mirrors PATCH /mark-imported response
    │   └── AuthModels.cs           — LoginRequest, AuthResponse, AuthUser, ProjectInfo, ProjectsResponse
    ├── PipelineSettings.cs         — EditorPrefs wrapper (API URL, session tokens, project, import path)
    ├── PipelineApiClient.cs        — HTTP client: Login, Refresh, GetUserProjects, GetApprovedAssets, MarkImported
    ├── AssetDownloader.cs          — download via proxy endpoint + AssetDatabase.ImportAsset
    └── PipelineImportWindow.cs     — EditorWindow: tela de login + Assets tab + Settings tab
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
| Email | Email da conta no Pipeline Tool (mesma usada na web app) |
| Password | Senha da conta |

Após login, o tool busca automaticamente os projetos do usuário e exibe um dropdown na aba Settings.

### Aba Settings (após login)

| Campo | Descrição |
|---|---|
| Signed in as | Nome do usuário logado + botão **Sign Out** |
| API Base URL | `http://localhost:3000` (dev) ou URL Vercel (prod) |
| Project | Dropdown com os projetos do usuário — salva imediatamente ao selecionar |
| Target Folder | Pasta destino dentro do projeto Unity, ex: `Assets/ImportedAssets` |

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
