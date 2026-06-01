# Pipeline Tool — Import Client

Unity Editor window para listar e importar assets 3D aprovados direto do Pipeline Tool.

- **Package name:** `com.antigravity.pipeline-tool`
- **Repositório:** https://github.com/VinniHashirama/pipeline-tool-unity
- **Unity mínimo:** 2021.3 LTS

---

## Instalação

### Via Package Manager UI (recomendado)

Abra **Window > Package Manager**, clique no botão **+** no canto superior esquerdo e escolha uma das opções abaixo.

#### Opção 1 — Git URL (direto do GitHub)

Escolha **"Add package from git URL…"** e cole:

```
https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0
```

Para fixar em uma versão específica, adicione `#v<semver>` ao final. Omita a tag para usar sempre o latest `main` (apenas em dev).

#### Opção 2 — Pasta local (desenvolvimento ativo)

Escolha **"Add package from disk…"** e navegue até o `package.json` dentro da pasta `unity-package/` clonada localmente.

### Via `Packages/manifest.json`

```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0"
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

Abra **Pipeline Tool > Import Window** na barra de menus do Unity.

Na primeira abertura, o tool exibe a **tela de login**.

### Tela de login

| Campo | Descrição |
|---|---|
| **Server** | Dropdown: **Local** (localhost:3000) · **Production** (pipeline-tool-web.vercel.app) · **Custom** (campo livre) |
| **Email** | Email da conta no Pipeline Tool (mesma usada na web app) |
| **Password** | Senha da conta |

Escolha o servidor antes de inserir as credenciais. A seleção é salva automaticamente no `EditorPrefs` — não precisa reconfigurar ao reabrir o Editor.

Após o login, o tool busca automaticamente os projetos do usuário.

### Aba Settings (após login)

| Campo | Descrição |
|---|---|
| Signed in as | Nome do usuário logado + botão **Sign Out** |
| Server | Mesmo picker do login — pode ser alterado sem sair |
| Project | Dropdown com os projetos disponíveis — salvo automaticamente ao selecionar |
| Target Folder | Pasta destino no projeto Unity (ex: `Assets/ImportedAssets`) · salvo com **Save Settings** |

Todas as configurações ficam no `EditorPrefs` — por usuário, por máquina. Nunca commitadas no repositório.

---

## Uso

1. Na aba **Assets**, clique em **Refresh** para buscar assets com status `approved`.
2. Clique em **Import** em qualquer linha para:
   - Baixar o arquivo para a pasta configurada
   - Executar `AssetDatabase.ImportAsset` (Unity importa automaticamente)
   - Chamar `PATCH /api/assets/{id}/mark-imported` → task vira `imported` e notificação Slack é enviada

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
git commit -m "chore: bump version to 0.2.0"
git push
git tag v0.2.0
git push origin v0.2.0
```

No Unity, atualize `manifest.json` para `#v0.2.0` — o Package Manager baixa a nova versão automaticamente.

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
        │   ├── AuthModels.cs        — LoginRequest, AuthResponse, ProjectInfo e afins
        │   └── ImportResult.cs      — response shape de PATCH /mark-imported
        ├── PipelineSettings.cs      — EditorPrefs: ambiente, tokens, projeto, pasta destino
        ├── PipelineApiClient.cs     — chamadas HTTP assíncronas (login, refresh, projetos, assets)
        ├── AssetDownloader.cs       — download via proxy + AssetDatabase refresh
        └── PipelineImportWindow.cs  — EditorWindow: login, aba Assets, aba Settings
```
