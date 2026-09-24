# Changelog

## [Unreleased]

### Added
- Pastas `Textures/` e `Materials/` para os tipos `texture` e `material`. Antes caíam em `Other/`.
  Sai antes do backend publicar esses tipos, para que o estúdio já esteja atualizado quando a
  primeira textura chegar — mudar a pasta depois do primeiro import deixaria o arquivo antigo órfão.

## [0.1.0] — Unreleased

### Added
- Editor window (`Hopper > Import Window`) to list approved assets
- Settings tab: API base URL, Supabase Anon Key, Project ID, import folder
- Download asset file to a configurable `Assets/` subfolder
- Mark-imported call to `PATCH /api/assets/{id}/mark-imported` after download
- Assembly definition `PipelineTool.Editor` (Editor-only)
