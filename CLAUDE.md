# CLAUDE.md — Hopper Unity Package

Unity UPM package providing an Editor window to import approved 3D assets from Hopper.

O nome visível é **Hopper**; os identificadores internos (package name, namespace
`AntiGravity.PipelineTool.*`, chaves de `EditorPrefs`, `.pipeline-manifest.json`) mantêm o
nome antigo de propósito — renomeá-los quebra instalações existentes. Ver `BRAND.md` na raiz
do workspace.
Part of a two-repo workspace — see the workspace root `CLAUDE.md` for the cross-repo architecture and API contract.

## Package identity

```
name:    com.antigravity.pipeline-tool
version: 0.5.0
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
    │   ├── PublishedItem.cs        — mirrors GET /api/items/published e POST /api/items/{id}/mark-imported (v0.3)
    │   └── AuthModels.cs           — LoginRequest, AuthResponse, AuthUser, ProjectInfo, ProjectsResponse, UserPermissions
    ├── PipelineSettings.cs         — EditorPrefs wrapper (API URL, session tokens, project, import path, auto-refresh sync flag, permissões)
    ├── PipelineApiClient.cs        — HTTP client: Login, Refresh, GetUserProjects, GetPermissions, GetApprovedAssets, GetProjectAssetsForSync, MarkImported, GetPublishedItems, MarkItemImported, Download, DownloadTexture (thumbnail, v0.4)
    ├── HopperStyles.cs             — estilos da janela (v0.4): card escuro, badge colorido por tipo, placeholder de thumbnail, nota âmbar — criados sob demanda, recriados após domain reload
    ├── PathSafety.cs               — TODO caminho escrito passa aqui: relativo, sem `..`, dentro do Target Folder, que fica dentro de Assets/
    ├── AssetDownloader.cs          — import por tipo: hierarquia local (TypePlural/Category?/AssetTitle/) + manifesto
    ├── ItemImporter.cs             — import por Item (v0.3): engine_path do servidor, plano De/Para e MoveAsset, labels
    ├── GitInfo.cs                  — `git rev-parse HEAD` do projeto, enviado como commit_hash
    ├── PipelineManifest.cs         — lê/grava .pipeline-manifest.json (chaveado por GUID) — mapeia asset local → asset_id/version_id/hash (+ item_id/engine_path, local_path)
    ├── PipelineSyncStatus.cs       — fonte única do estado de sync: refresh contra o servidor (cache em SessionState) + Recompute local (disco + manifesto) + ícones na aba Project
    ├── PipelineAssetWatcher.cs     — AssetPostprocessor: mudança sob o Target Folder → Recompute (arquivo apagado vira Missing na hora, sem rede) (v0.5)
    ├── AssetRestorer.cs            — Reimport (baixa de novo a versão publicada, com o GUID original) e Forget (tira do manifesto); WriteAsset usado pelos dois imports (v0.5)
    ├── HopperSyncOverlay.cs        — overlay no Scene View: sapinho + bolinha + resumo + botão de refresh (v0.5)
    └── PipelineImportWindow.cs     — EditorWindow: tela de login + Assets tab (lista de Itens com thumbnail e Import/Update, Refresh / Sync Status) + Settings tab
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

- **Add package from git URL** → `https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.3.0`
- **Add package from disk** → navegue até o `package.json` da pasta local

### `Packages/manifest.json`

```json
// Pinado em versão (QA / produção)
"com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.3.0"

// Latest main (só em dev, sem pin)
"com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git"

// Caminho local (desenvolvimento ativo)
"com.antigravity.pipeline-tool": "file:C:/caminho/absoluto/para/unity-package"
```

## Configuração

Abra **Hopper > Import Window**. Na primeira abertura, o tool exibe uma tela de login.

### Tela de Login

| Campo | Descrição |
|---|---|
| Server | Dropdown: **Local** (localhost:3000) · **Production** (URL Vercel) · **Custom** (campo livre) |
| Email | Email da conta no Hopper (mesma usada na web app) |
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

Sem `version_id`, o servidor entrega a versão **publicada** do asset (o ponteiro `published_version_id`,
desde a migration 005 do web app). O servidor busca o arquivo no storage correto (Supabase ou GDrive) e entrega como stream. Isso resolve dois problemas:
- **Supabase Storage:** bucket privado — URLs públicas não funcionam sem auth
- **Google Drive:** `file_url` no banco é um path relativo (`/api/gdrive/file/{id}`), sem hostname

## Hierarquia de pastas local

O `AssetDownloader` cria automaticamente a hierarquia de pastas no projeto Unity espelhando a estrutura do Pipeline Tool:

```
[Target Folder]/
  Props/
    Industrial/          ← categoria (se definida)
      PROP_Conteiner/    ← título do asset
        SM_Conteiner.fbx ← file_name ESTÁVEL: o mesmo em toda versão
  Characters/
    CH_Guerreiro/
      SK_Guerreiro.fbx
  Textures/
    T_Conteiner_Diffuse/
      T_Conteiner_Diffuse.png
```

**Mapeamento de tipos:**
`prop → Props` · `character → Characters` · `environment → Environments` · `vfx → VFX` · `ui → UI` · `audio → Audio` · `texture → Textures` · `material → Materials` · `other → Other` (textura e material desde a v0.2.0)

**O nome do arquivo não muda entre versões** — desde a migration 006 do web app, o servidor garante
que toda versão de um asset tem o mesmo `file_name` (o storage guarda `SM_Conteiner_v3.fbx`, mas
o Unity recebe `SM_Conteiner.fbx`). Por isso importar a v4 **substitui** o arquivo da v3 no mesmo
caminho, preservando o GUID e as referências de prefab e material.

**O que ainda muda o caminho:** tipo, categoria e título do asset entram no path. Trocar qualquer
um deles depois do import faz o próximo download cair num caminho novo, e o arquivo antigo fica
órfão no projeto — o mesmo vale para "Renomear asset" no web app, que avisa isso na tela.

Assets sem categoria vão direto sob o tipo: `Props/PROP_Cadeira/file.fbx`.

### Projetos por Item (v0.3)

Num projeto organizado por Item, **o servidor decide o caminho**: `GET /api/items/published` manda
um `engine_path` por arquivo (`Props/Industrial/PR001_Chair/SM_PR001_Chair.fbx`), e o plugin só
confere, em `PathSafety.Combine`, que ele fica dentro do Target Folder. Importar um Item traz todos
os arquivos dele que vão para a engine e marca em lote (`POST /api/items/{id}/mark-imported`), com
o `HEAD` do repo como commit hash. Cada arquivo ganha labels `Hopper`, tipo e categoria — sem label por Item (seria uma por objeto), e as labels postas à mão ficam.

**Fora do lugar:** o Hopper é a fonte da verdade do caminho. No Refresh, se o manifesto conhece um
arquivo num caminho diferente do `engine_path` (alguém arrastou a pasta à mão), a janela mostra
"N Item(s) changed place" com De/Para e o botão **Move**. Hoje o `engine_path` de um Item não muda:
a troca de categoria (D12 do `item-architecture.md`) foi adiada, e quando voltar usa este mesmo
aviso. Mover usa `AssetDatabase.MoveAsset`
(preserva GUID e referências) e apaga as pastas que ficaram vazias. **Nunca move sem clique.** O
movimento vai para as outras máquinas pelo Git — arquivos, `.meta` e manifesto —, e lá o manifesto
já bate com o servidor, então ninguém move de novo.

**A linha do Item (v0.4):** a lista mostra todos os Itens do feed. O botão sai do manifesto local:
**Import** quando nenhum arquivo do Item é conhecido aqui, **Update** quando algum é e o servidor
tem versão maior (ou um arquivo ainda não marcado como importado), "✓ Imported" no resto. Import e
Update são a mesma chamada (`ImportItemAsync`): move primeiro, depois reescreve no mesmo
`engine_path` — mesmo GUID, as referências de cena e prefab seguem valendo.

**Thumbnails (v0.4):** cada Item traz `thumbnail_url`, um caminho **relativo**
(`/api/assets/{asset_id}/thumbnail`) buscado em `ApiBaseUrl + thumbnail_url` com os mesmos headers
de auth, como o download — nunca a URL do storage. `DownloadTextureAsync` devolve `null` em vez de
lançar (offline, 404, WebP que o Unity não decodifica), e a linha fica com o placeholder (sigla do
tipo). O cache da janela é por URL, uma requisição por vez, destruído no `OnDisable`, no Sign Out e
na troca de projeto; um Refresh tenta de novo as que falharam.

**Path traversal fechado nas duas rotas:** o fluxo por tipo também passa por `PathSafety` desde a
v0.3 (antes, um `file_name` com `../` escrevia fora da pasta, e o Target Folder podia apontar para
fora de `Assets/`).

O **Target Folder** é configurável na aba Settings (default: `Assets/ImportedAssets`). O nome do projeto **não** faz parte do path local — a organização por projeto fica por conta do Target Folder escolhido pelo tech artist.

Os modelos `ApprovedAsset` e `AssetCategory` em `Models/ApprovedAsset.cs` espelham o response do servidor (campo `category` é nullable).

## Sync Status

Depois de importar um asset, o tool grava uma entrada em `.pipeline-manifest.json` (na raiz do **Target Folder**) associando o asset local à sua origem no Hopper. O estado de cada entrada sai de três fontes: o **manifesto**, o **disco** e o **feed do servidor** (o que está publicado). A aba Project mostra um ícone sobre cada asset rastreado e sobre as pastas acima dele (o pior estado de dentro):

| Ícone | Estado | Significado |
|---|---|---|
| `✓` verde | Synced | O arquivo local bate com a versão **publicada** no servidor |
| `!` laranja | Outdated | O servidor publicou uma versão mais nova do que a baixada localmente |
| `M` azul | Modified Locally | O conteúdo do arquivo local mudou desde o download (hash diverge) — provável edição manual fora do pipeline |
| `×` vermelho | Missing (v0.5) | O manifesto conhece o arquivo, mas ele sumiu do disco. Aparece na pasta mais próxima que ainda existe |

Dois contadores só existem no overlay do Scene View, porque não têm arquivo onde desenhar:
**new** (publicado no Hopper, fora do manifesto — falta importar) e **unknown** (no manifesto, mas
este servidor não publica: despublicado, ou importado com o server picker apontando para outro
ambiente). Um arquivo *unknown* não ganha `✓`.

### Arquivo apagado localmente (v0.5)

**A deleção é só local, de propósito.** O servidor não fica sabendo: o `imported_at` da web é
histórico ("alguém trouxe essa versão"), e uma máquina com um arquivo apagado e ainda não
commitado não deve mudar o estado para o time todo. A verdade local é o manifesto + o disco.

- **Detecção:** `PipelineAssetWatcher` (um `AssetPostprocessor`) chama `PipelineSyncStatus.Recompute()`
  quando algo sob o Target Folder é importado, apagado ou movido. Apagar no Unity vira `×` na hora,
  sem rede; apagar pelo Explorer aparece quando o Unity percebe (próximo refresh de assets). **A
  entrada nunca sai sozinha do manifesto** — apagar sem querer é justamente o que queremos acusar.
- **Reimport** (faixa "N file(s) tracked by Hopper were deleted locally" na aba Assets, ou o botão
  **Reimport** na linha do Item): baixa de novo **a versão publicada agora, no caminho que o Hopper
  manda** — não a versão e o caminho que o manifesto lembra, que podem não existir mais (versão
  apagada no Hopper, título ou categoria trocados). Só um asset que o Hopper não publica mais cai no
  que o manifesto registrou. Antes do import, **grava um `.meta` mínimo com o GUID original**
  (`AssetRestorer.WriteAsset`): referências de cena e prefab voltam a valer e a chave do manifesto
  continua a mesma. Se a versão baixada é mais nova que a apagada, marca como importada no servidor,
  como qualquer import. As import settings voltam ao padrão — se a deleção ainda não foi commitada,
  `git checkout` do arquivo e do `.meta` traz tudo de volta. O mesmo `WriteAsset` roda nos dois
  imports, então um Update sobre arquivo apagado também mantém o GUID.
- **Glifos:** a fonte do Editor não tem todos os símbolos Unicode — o `⟳` saiu como um botão vazio no
  overlay e o `✕` corria o mesmo risco. O refresh usa o ícone `Refresh` do Editor
  (`EditorGUIUtility.IconContent`), e o Missing usa `×` (Latin-1). Símbolo novo na UI: prefira ícone do
  Editor ou caractere Latin-1.
- **Forget:** apagou de propósito. As entradas saem do manifesto (commitar), e o asset volta a contar
  como *new* se continuar publicado.
- **Onde escrever de volta:** `local_path` (v0.5), gravado no import e atualizado em cada Recompute
  (acompanha moves). Entradas anteriores sem ele caem no `engine_path` (Item) ou no caminho que o
  import por tipo usaria hoje (`AssetDownloader.BuildDestPath`, a partir do feed). O primeiro
  Recompute depois de instalar a v0.5 preenche `local_path` em todas as entradas — um diff único no
  manifesto do jogo, para commitar.

### Formato do manifesto

`.pipeline-manifest.json` é um arquivo único, **commitado no repositório do jogo** (visibilidade compartilhada entre o time), chaveado pelo **GUID** do asset — não pelo path. O GUID é gerido pelo próprio Unity via `.meta`; o tool o lê via `AssetDatabase` e só escreve um `.meta` num caso: o Reimport de um arquivo apagado, quando não existe `.meta` nenhum no caminho. Renomear/mover um asset rastreado no Unity não quebra a referência.

```json
{
  "entries": [
    { "guid": "a1b2c3...", "asset_id": "uuid", "version_id": "uuid", "version_number": 3, "task_title": "PROP_Conteiner", "local_hash": "sha1...",
      "local_path": "Assets/ImportedAssets/Props/PROP_Conteiner/SM_Conteiner.fbx" },
    { "guid": "d4e5f6...", "asset_id": "uuid", "version_id": "uuid", "version_number": 1, "task_title": "SM_PR001_Chair.fbx", "local_hash": "sha1...",
      "item_id": "uuid", "engine_path": "Props/Industrial/PR001_Chair/SM_PR001_Chair.fbx",
      "local_path": "Assets/ImportedAssets/Props/Industrial/PR001_Chair/SM_PR001_Chair.fbx" }
  ]
}
```

Como é um arquivo único compartilhado, existe risco residual de conflito de merge quando duas pessoas importam/atualizam assets diferentes em paralelo — mitigado mantendo as entradas sempre ordenadas por GUID antes de salvar (ajuda o merge automático do git). Na prática é um conflito de JSON simples de resolver manualmente. Se isso virar recorrente, um merge driver customizado é a próxima etapa — não implementado agora.

> **⚠️ Ícones não estão aparecendo** (registrado em 24/set/2026, antes da migration 005 do web app).
> O servidor responde certo. Suspeita: o manifesto do `dungeon-delivery` mistura entradas de
> **produção e de staging** (imports feitos com o server picker em cada um), e o refresh pode estar
> falhando antes de desenhar. Desde a v0.5 o overlay do Scene View mostra o erro do refresh e conta
> as entradas *unknown* (as de outro ambiente) — deve bastar para confirmar ou descartar. Enquanto isso,
> `pipeline-tool/scripts/check-manifest.mjs` confere um manifesto contra o banco de forma objetiva.

### Quando o estado é calculado

Nunca dentro do callback de desenho (`projectWindowItemOnGUI`, que roda a cada repaint da aba Project) — isso só lê os dicionários já calculados. O `PipelineSyncStatus` tem duas metades:

- **`RefreshAsync` (rede):** os mesmos dois feeds de sempre (`/api/assets/approved?include_imported=true`
  e `/api/items/published?include_imported=true`). O resultado fica em `SessionState`, que sobrevive
  ao domain reload (todo compile de script) mas não a fechar o Editor, e só vale para o projeto e o
  servidor de onde veio. Dispara:
  - ao abrir o Editor (e ao abrir a Import Window), **só se não houver cache** para o projeto atual,
    e se **Auto-refresh on Editor start** estiver ligado (default: `true`);
  - pelo botão de refresh do overlay e pelo botão **Sync Status** da janela.

  Falha não lança: vira `Summary.Error` ("Offline — could not reach Hopper", "Session expired — sign
  in again"), e os contadores anteriores continuam valendo.
- **`Recompute` (disco + manifesto, sem rede):** depois de cada refresh, ao abrir o Editor, depois de
  cada import/Reimport/Forget e sempre que o watcher vê mudança no Target Folder. O hash só é relido
  quando tamanho ou mtime do arquivo mudaram. Dispara o evento `Changed`, que a janela e o overlay escutam.

O manifesto só é relido do disco quando não há mudança pendente em memória (`HasUnsavedChanges`):
um import grava entradas com `save: false` entre um download e outro, e um Recompute no meio não pode
jogá-las fora.

### Overlay no Scene View (v0.5)

`HopperSyncOverlay` — uma linha num canto do Scene View: **sapinho** à esquerda, **bolinha** com o pior
estado, **resumo** e o botão de **refresh** (o ícone `Refresh` do próprio Editor).

```
[🐸] ● All synced                          [↻]
[🐸] ● 2 to update · 1 missing · 3 new     [↻]
```

- Bolinha: vermelha (missing) › azul (modified) › laranja (to update / new) › verde (tudo em dia,
  conferido contra o servidor) › cinza (não conferido, erro, unknown, deslogado).
- O refresh só reconfere a sincronia — **nunca importa**. Clicar no texto abre a Import Window. O tooltip
  lista os arquivos apagados (até 8), o horário da última conferência e o erro, se houver.
- Deslogado ou sem projeto: "Sign in to Hopper" / "Select a project in Hopper", com o refresh desligado.
- Nasce no canto de baixo à esquerda (`DockZone.LeftColumn` + `DockPosition.Bottom`) a partir da
  2022.3; antes disso esses campos do `OverlayAttribute` não existem, e ele abre flutuando — encaixe
  uma vez, o Unity lembra. Liga e desliga pelo menu de overlays do Scene View (tecla de acento grave).
- O sapinho é a marca fixa da barra; a mensagem de erro fica neutra, sem pose (regra do `BRAND.md`).

## Releases

Sempre bump `version` em `package.json` antes de taggear. Use `v<semver>`.

| Versão | Data | O que muda |
|---|---|---|
| `v0.5.0` | 29/set/2026 | Sincronia de verdade: estado **Missing** (`×`) para arquivo apagado localmente, detectado na hora pelo `PipelineAssetWatcher`; **Reimport** (versão publicada, com o GUID original) e **Forget**; overlay no Scene View com sapinho, resumo e refresh; cache do feed em `SessionState` (os ícones sobrevivem ao compile sem a janela aberta); erro do refresh visível; `local_path` no manifesto. Só local — nenhuma mudança no web app |
| `v0.4.0` | 28/set/2026 | Visual novo da janela: lista em card com thumbnail, badge colorido por tipo e um botão por Item — **Import** (nada local), **Update** (versão nova, subtítulo `v2 → v3`) ou "✓ Imported" (os em dia ficam num foldout). Thumbnail via `/api/assets/{id}/thumbnail`; contra um servidor sem `thumbnail_url`, só mostra o placeholder |
| `v0.3.0` | 28/set/2026 | Import por Item com `engine_path` do servidor, mover de volta com confirmação, `PathSafety` nos dois fluxos, commit hash real, token renovado antes do download, labels. Precisa do web com a Fase 4 (`/api/items/published`); contra um servidor sem ela, a lista de Itens só fica vazia |
| `v0.2.0` | 25/set/2026 | Pastas `Textures/` e `Materials/`. Publicada **antes** do backend servir texturas, para que nenhuma textura fosse importada em `Other/` e ficasse órfã na atualização |
| `v0.1.0` | — | Primeira versão |

```bash
# Edite package.json: "version": "0.5.0"
git add package.json package.json.meta
git commit -m "chore: bump version to 0.5.0"
git push
git tag v0.5.0
git push origin v0.5.0
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
