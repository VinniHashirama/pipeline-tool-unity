# CLAUDE.md — Pipeline Tool Unity Package

Unity UPM package providing an Editor window to import approved 3D assets from Pipeline Tool.

## Package identity

```
name:    com.antigravity.pipeline-tool
version: 0.1.0
unity:   2021.3+
```

## Structure

```
Editor/
├── PipelineTool.Editor.asmdef      — Editor-only assembly
└── Scripts/
    ├── Models/
    │   ├── ApprovedAsset.cs        — mirrors GET /api/assets/approved response
    │   └── ImportResult.cs         — mirrors PATCH /mark-imported response
    ├── PipelineSettings.cs         — EditorPrefs wrapper (API URL, keys, paths)
    ├── PipelineApiClient.cs        — async HTTP via UnityWebRequest
    ├── AssetDownloader.cs          — file download + AssetDatabase.ImportAsset
    └── PipelineImportWindow.cs     — EditorWindow: Assets tab + Settings tab
```

## Dev workflow — IMPORTANTE

**Nunca desenvolva o package com ele instalado via Git URL.** A pasta de cache do UPM é imutável — o Unity não consegue gerar `.meta` files lá, resultando em erros de importação.

O ciclo correto é sempre:

```
1. Editar arquivos no package (VS Code, Rider, etc.)
2. Abrir o projeto Dev no Unity (instalado via file:)
3. Unity detecta mudanças e compila automaticamente
4. Ao criar arquivos novos → Unity gera os .meta correspondentes
5. Testar na Engine
6. git add . && git commit (inclui os .meta novos)
7. git push
8. Se for release: git tag v0.x.0 && git push origin v0.x.0
```

### Por que os `.meta` files precisam estar no repo

O Unity usa GUIDs nos `.meta` para referenciar assets internamente. Sem eles, o package instalado via Git URL mostra erro:
```
Asset has no meta file, but it's in an immutable folder. The asset will be ignored.
```

O `.gitignore` deste repo já está configurado para **não ignorar** `.meta` files — isso é intencional.

### Estrutura de projetos Unity recomendada

```
d:\Projects\
├── PipelineTool\               ← workspace com os dois repos
│   ├── unity-package\          ← repo do package (file: aponta aqui)
│   └── pipeline-tool\          ← repo web
└── PipelineTool-Unity-Dev\     ← projeto Unity (fora do workspace, não vai pro Git)
    └── Packages\
        └── manifest.json       ← "com.antigravity.pipeline-tool": "file:..."
```

O projeto Dev não precisa estar no Git — é uma sandbox local para validar antes de taggear.

### Ao criar novos arquivos no package

```bash
# 1. Crie o arquivo (no VS Code / Rider)
# 2. Abra o projeto Dev no Unity — ele vai gerar o .meta automaticamente
# 3. Verifique que o .meta aparece no git status
git status   # deve aparecer NomeDoArquivo.cs.meta como untracked
git add NomeDoArquivo.cs NomeDoArquivo.cs.meta
git commit -m "feat: ..."
```

Nunca commite um `.cs` sem o `.meta` correspondente.

## Instalação em outros projetos

**Local (desenvolvimento)** — `Packages/manifest.json`:
```json
"com.antigravity.pipeline-tool": "file:C:/caminho/absoluto/para/unity-package"
```

**Git URL pinado (QA / produção)** — `Packages/manifest.json`:
```json
"com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0"
```

**Via Package Manager UI:** `+` → *Add package from git URL* → cole a URL acima.

## Configuração

Open **Pipeline Tool > Import Window** → Settings tab:

| Campo | Descrição |
|---|---|
| API Base URL | `http://localhost:3000` (dev) ou URL Vercel (prod) |
| Supabase Anon Key | Supabase Dashboard → Project Settings → API |
| Pipeline API Key | Valor de `UNITY_TOOL_API_KEY` configurado no servidor web |
| Project ID | UUID do projeto no Pipeline Tool (opcional — filtra assets) |
| Target Folder | Pasta destino, ex: `Assets/ImportedAssets` |

Settings ficam no `EditorPrefs` — por usuário, por máquina. Nunca commitados.

## Release tags

Sempre bump `version` em `package.json` antes de taggear.

```bash
git tag v0.1.0
git push origin v0.1.0
```
