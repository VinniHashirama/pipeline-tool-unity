# Changelog

## [0.3.0] — não publicada

### Added
- Import por Item (projetos organizados por Item no Hopper): lista de Itens na aba Assets, "Import"
  traz todos os arquivos do Item que vão para a engine, no `engine_path` que o servidor manda.
- "Item(s) changed place": quando a categoria de um Item muda no Hopper (ou a pasta foi arrastada à
  mão), a janela mostra De/Para e move com `AssetDatabase.MoveAsset` — só com clique. Preserva GUID
  e referências; pastas que ficam vazias são removidas.
- Labels nos arquivos de Item: `Hopper`, código, tipo e categoria.
- Commit hash real no mark-imported (`git rev-parse HEAD` do projeto).

### Fixed
- Path traversal: um `file_name` com `../` vindo do servidor escrevia fora da pasta, e o Target Folder
  aceitava caminho fora de `Assets/`. Todo caminho passa agora por `PathSafety`.
- O download renova o token antes de começar (uma sessão longa falhava o download).

## [0.2.0] — 2026-09-25

### Added
- Pastas `Textures/` e `Materials/` para os tipos `texture` e `material`. Antes caíam em `Other/`.
  Sai antes do backend publicar esses tipos, para que o estúdio já esteja atualizado quando a
  primeira textura chegar — mudar a pasta depois do primeiro import deixaria o arquivo antigo órfão.

## [0.1.0]

### Added
- Editor window (`Hopper > Import Window`) to list approved assets
- Settings tab: API base URL, Supabase Anon Key, Project ID, import folder
- Download asset file to a configurable `Assets/` subfolder
- Mark-imported call to `PATCH /api/assets/{id}/mark-imported` after download
- Assembly definition `PipelineTool.Editor` (Editor-only)
