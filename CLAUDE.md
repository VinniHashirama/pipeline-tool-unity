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
    │   └── ImportResult.cs         — mirrors PATCH /mark-imported response
    ├── PipelineSettings.cs         — EditorPrefs wrapper (API URL, keys, paths)
    ├── PipelineApiClient.cs        — async HTTP via UnityWebRequest + TaskCompletionSource
    ├── AssetDownloader.cs          — file download + AssetDatabase.ImportAsset
    └── PipelineImportWindow.cs     — EditorWindow: Assets tab + Settings tab
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

Abra **Pipeline Tool > Import Window** → aba **Settings**:

| Campo | Descrição |
|---|---|
| API Base URL | `http://localhost:3000` (dev) ou URL Vercel (prod) |
| Supabase Anon Key | Supabase Dashboard → Project Settings → API → `anon public` |
| Pipeline API Key | Valor de `UNITY_TOOL_API_KEY` configurado no servidor web |
| Project ID | UUID do projeto no Pipeline Tool (opcional — filtra assets por projeto) |
| Target Folder | Pasta destino dentro do projeto Unity, ex: `Assets/ImportedAssets` |

Settings ficam no `EditorPrefs` — por usuário, por máquina. Nunca commitados.

## Autenticação

O tool envia o `Pipeline API Key` como header `X-Pipeline-Key` em todas as requisições. O servidor valida contra a env var `UNITY_TOOL_API_KEY`.

**Importante:** o `Supabase Anon Key` visível nas Settings **não é** usado como auth token — ele é apenas o identificador público do projeto Supabase (e pode ser necessário para futuras chamadas diretas ao Supabase). A autenticação real com o servidor web é feita **exclusivamente** pelo `Pipeline API Key`.

Quando autenticado via API key, o servidor usa `createAdminClient()` (service role) para contornar o RLS do Supabase. O `activity_log` registra a importação com `user_id = null` e `imported_by = "Unity Import Tool"`.

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
