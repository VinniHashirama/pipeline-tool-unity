# Pipeline Tool — Import Client

Unity Editor window for listing and importing approved 3D assets from Pipeline Tool.

- **Package name:** `com.antigravity.pipeline-tool`
- **Repository:** https://github.com/VinniHashirama/pipeline-tool-unity
- **Minimum Unity:** 2021.3 LTS

---

## Installation

### Via Package Manager UI (recomendado)

Abra **Window > Package Manager** no Unity, clique no botão **+** no canto superior esquerdo e escolha uma das opções abaixo.

---

#### Opção 1 — Git URL (direto do GitHub)

No Package Manager, escolha **"Add package from git URL…"** e cole:

```
https://github.com/VinniHashirama/pipeline-tool-unity.git
```

Isso sempre instala o commit mais recente do `main`. Para fixar em uma versão específica, adicione `#tag` ao final:

```
https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0
```

> Cada release deve ter uma tag `v<semver>` no repositório. Veja a seção **Versionamento** abaixo.

---

#### Opção 2 — Pasta local (desenvolvimento)

No Package Manager, escolha **"Add package from disk…"** e navegue até o arquivo `package.json` dentro da pasta `unity-package/` clonada localmente.

---

### Via `Packages/manifest.json` (alternativa manual)

Abra `Packages/manifest.json` do seu projeto Unity e adicione à seção `"dependencies"`:

**Git URL (pinado):**
```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0"
  }
}
```

**Git URL (sempre latest main — use só em dev):**
```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git"
  }
}
```

**Caminho local (para contribuição ativa no package):**
```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "file:C:/caminho/absoluto/para/unity-package"
  }
}
```

No Windows use barras normais `/` ou barras duplas `\\`. O UPM recarrega automaticamente quando você salva um arquivo dentro da pasta.

---

## Configuração

1. Abra **Pipeline Tool > Import Window** na barra de menus do Unity.
2. Vá para a aba **Settings**.
3. Preencha os campos:

| Campo | Descrição |
|---|---|
| API Base URL | `http://localhost:3000` (dev) ou a URL do Vercel (prod) |
| Supabase Anon Key | Supabase Dashboard → Project Settings → API → `anon public` |
| Pipeline API Key | Valor de `UNITY_TOOL_API_KEY` configurado no servidor |
| Project ID | UUID do projeto no Pipeline Tool (opcional — filtra assets) |
| Target Folder | Pasta destino dentro do projeto Unity, ex: `Assets/ImportedAssets` |

4. Clique em **Save Settings**. Os valores ficam no `EditorPrefs` — por usuário, por máquina, nunca commitados.

---

## Uso

1. Na aba **Assets**, clique em **Refresh** para buscar os assets com status `approved`.
2. Clique em **Import** em qualquer linha para:
   - Baixar o arquivo para a pasta configurada.
   - Disparar o `AssetDatabase.ImportAsset` (Unity importa automaticamente).
   - Chamar `PATCH /api/assets/{id}/mark-imported` → task vira `imported` e notificação Slack é enviada.

---

## Versionamento

Este package usa tags `v<semver>` independentes do web app:

```bash
# Depois de alterar e fazer push no main:
git tag v0.2.0
git push origin v0.2.0
```

No Unity, atualize a referência em `manifest.json` para `#v0.2.0` e salve — o Package Manager baixa a nova versão automaticamente.

---

## Estrutura do package

```
com.antigravity.pipeline-tool/
├── package.json
├── CHANGELOG.md
├── README.md
└── Editor/
    ├── PipelineTool.Editor.asmdef
    └── Scripts/
        ├── Models/
        │   ├── ApprovedAsset.cs     — response shape de GET /api/assets/approved
        │   └── ImportResult.cs      — response shape de PATCH /mark-imported
        ├── PipelineSettings.cs      — EditorPrefs (URL, keys, pasta destino)
        ├── PipelineApiClient.cs     — chamadas HTTP assíncronas via UnityWebRequest
        ├── AssetDownloader.cs       — download do arquivo + AssetDatabase refresh
        └── PipelineImportWindow.cs  — EditorWindow: abas Assets e Settings
```
