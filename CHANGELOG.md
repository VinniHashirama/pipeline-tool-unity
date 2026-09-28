# Changelog

## [0.4.0] — não publicada

### Changed
- Visual novo da aba Assets: lista em card escuro, cada Item com thumbnail, badge colorido do tipo,
  nome, subtítulo e um botão só. Cabeçalho `{Projeto} · N to import`.
- O botão sai do manifesto local: **Import** (nenhum arquivo do Item aqui), **Update** (versão nova no
  servidor — o subtítulo mostra `SK_CH001_Hero.fbx v2 → v3`) ou "✓ Imported". Os Itens em dia ficam
  num foldout "Up to date (N)". Quando há Update, uma nota lembra que o arquivo é substituído no lugar
  e as referências de cena e prefab continuam valendo.
- A lista de assets por tipo (legado) ganhou o mesmo visual, com placeholder no lugar da thumbnail.

### Added
- Thumbnail do Item via `GET /api/assets/{id}/thumbnail` (campo `thumbnail_url`, relativo, com os
  mesmos headers de auth). Carrega sob demanda e cai no placeholder quando falha ou quando o
  servidor ainda não manda o campo.

## [0.3.0] — 2026-09-28

### Added
- Import por Item (projetos organizados por Item no Hopper): lista de Itens na aba Assets, "Import"
  traz todos os arquivos do Item que vão para a engine, no `engine_path` que o servidor manda.
- "Item(s) changed place": quando um arquivo de Item não está onde o Hopper diz (pasta arrastada à
  mão), a janela mostra De/Para e move de volta com `AssetDatabase.MoveAsset` — só com clique.
  Preserva GUID e referências; pastas que ficam vazias são removidas.
- Labels nos arquivos de Item: `Hopper`, tipo e categoria (sem label por Item, para não inundar a lista). Labels postas à mão são mantidas.
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
