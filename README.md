# Pipeline Tool — Import Client

Unity Editor window for listing and importing approved 3D assets from Pipeline Tool.

## Requirements

- Unity 2021.3 LTS or newer
- Pipeline Tool web app running (local or hosted)

## Installation

### Option A — Local path (development, no Git required)

In your Unity project, open `Packages/manifest.json` and add:

```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "file:C:/path/to/PipelineTool/unity-package"
  }
}
```

Use the absolute path to the `unity-package/` folder on your machine.
The package reloads automatically whenever you save a script inside it.

### Option B — GitHub URL with subfolder (staging / QA project)

```json
{
  "dependencies": {
    "com.antigravity.pipeline-tool": "https://github.com/YOUR_ORG/PipelineTool.git?path=/unity-package#unity/v0.1.0"
  }
}
```

- `?path=/unity-package` tells UPM which subfolder is the package root.
- `#unity/v0.1.0` pins a specific tag. Omit it to track the latest commit on the default branch (not recommended for QA).
- Unity will clone the full repository but only surface the `unity-package/` folder as a package.

> **Tag convention:** use `unity/v<semver>` for package releases to distinguish them from web app release tags.

## Configuration

1. Open **Pipeline Tool > Import Window** in the Unity menu bar.
2. Go to the **Settings** tab.
3. Fill in:
   - **API Base URL** — e.g. `http://localhost:3000` or your Vercel URL.
   - **Supabase Anon Key** — found in Supabase Dashboard → Project Settings → API.
   - **Pipeline API Key** — optional; only needed if the server validates `X-Pipeline-Key`. See note below.
   - **Project ID** — UUID of the Pipeline Tool project to filter by (optional).
   - **Target Folder** — where downloaded files land, e.g. `Assets/ImportedAssets`.
4. Click **Save Settings**. Values are stored in `EditorPrefs` (per-user, per-machine).

### Authentication note

The current server API validates user sessions via `supabase.auth.getUser()`.
The Supabase Anon Key is **not** a user session token — sending it as a Bearer token will return 401.

Two paths to fix this before the tool can reach the server:

**Path 1 (recommended):** Add a simple API key check to the server:

```typescript
// In each API route, replace the auth check with:
const pipelineKey = request.headers.get('X-Pipeline-Key')
if (pipelineKey !== process.env.UNITY_TOOL_API_KEY) {
  return NextResponse.json({ error: 'Unauthorized' }, { status: 401 })
}
```

Add `UNITY_TOOL_API_KEY=<random-secret>` to `.env.local` and paste the same value into the **Pipeline API Key** field in Settings.

**Path 2:** Authenticate with a dedicated service account; the tool would need a login flow to exchange email/password for a session JWT.

## Usage

1. Go to the **Assets** tab and click **Refresh**.
2. The list shows all tasks with status `approved` from Pipeline Tool.
3. Click **Import** on a row to:
   - Download the file to the configured target folder.
   - Trigger an `AssetDatabase` refresh (Unity imports the file automatically).
   - Call `PATCH /api/assets/{id}/mark-imported`, transitioning the task to `imported`.

## Repository structure

```
PipelineTool/
├── pipeline-tool/          Next.js web app
└── unity-package/          This UPM package
    ├── package.json
    ├── Editor/
    │   ├── PipelineTool.Editor.asmdef
    │   └── Scripts/
    │       ├── Models/
    │       │   ├── ApprovedAsset.cs
    │       │   └── ImportResult.cs
    │       ├── PipelineSettings.cs
    │       ├── PipelineApiClient.cs
    │       ├── AssetDownloader.cs
    │       └── PipelineImportWindow.cs
    └── README.md
```
