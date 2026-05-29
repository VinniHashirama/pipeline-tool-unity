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

## Adding to a Unity project

**Local (development)** — edit `Packages/manifest.json`:
```json
"com.antigravity.pipeline-tool": "file:C:/absolute/path/to/unity-package"
```

**GitHub (staging/QA)** — edit `Packages/manifest.json`:
```json
"com.antigravity.pipeline-tool": "https://github.com/VinniHashirama/pipeline-tool-unity.git#v0.1.0"
```

Use `#main` to track the tip of main (no pin). Use a tag like `#v0.1.0` to pin a release.

## Configuration

Open **Pipeline Tool > Import Window** → Settings tab:

| Field | Description |
|---|---|
| API Base URL | `http://localhost:3000` for dev, Vercel URL for prod |
| Supabase Anon Key | Supabase Dashboard → Project Settings → API |
| Pipeline API Key | Value of `UNITY_TOOL_API_KEY` on the server (preferred auth) |
| Project ID | UUID of the Pipeline Tool project to filter (optional) |
| Target Folder | Where downloaded files land, e.g. `Assets/ImportedAssets` |

Settings are stored in `EditorPrefs` — per-user, per-machine. Never committed.

## Authentication — known issue

The current server routes call `supabase.auth.getUser()` which requires a user session JWT.
Sending the Supabase Anon Key as Bearer returns 401.

**Fix required in `pipeline-tool-web`** — both route handlers need to accept `X-Pipeline-Key`:
```typescript
const key = request.headers.get('X-Pipeline-Key')
if (key !== process.env.UNITY_TOOL_API_KEY) {
  return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
}
```

Until this is implemented, the Import Window will return 401 on Refresh/Import.

## Release tags

Use `v<semver>` tags, e.g. `v0.1.0`. Always bump `version` in `package.json` before tagging.

```bash
git tag v0.1.0
git push origin v0.1.0
```
