# Changelog

## [0.8.0] — 2026-11-13

### Changed
- **Projeto e Target Folder vão para o Git.** Ficam em `ProjectSettings/HopperSettings.json`, commitado com o
  jogo: quem clona já vem no projeto Hopper e na pasta certos, e ninguém importa de outro projeto no mesmo
  manifesto. Antes ficavam no `EditorPrefs`, que é da máquina e valia para todos os projetos Unity dela.
- **Servidor fixo em produção.** O seletor Local/Production/Custom saiu da tela de login. O override de dev só
  aparece com o plugin instalado por `file:`, fica só na máquina e mostra uma faixa amarela em todas as abas.
  Quem tinha "Local" salvo passa a ir para produção (o default antigo era Local).
- Trocar o projeto ou a pasta pede confirmação ("muda para o time todo") e lembra de commitar o arquivo.
- Sign Out não esquece mais o projeto (é do projeto Unity, não da sessão).
- O cache de permissões guarda de qual projeto veio; o de outro projeto conta como "não carregou".
- Refresh sem projeto ligado é recusado (antes trazia os assets de todos os projetos).

### Added
- Aviso quando o projeto ligado não está entre os da conta, em vez de mostrar o primeiro da lista como se
  fosse o ligado.
- Migração da v0.7: sem o arquivo, a aba Settings oferece usar o projeto e a pasta que a máquina guardava.
  Nunca aplica sozinha. Com um projeto só na conta e nada a migrar, liga direto (e avisa para commitar).

### Notes
- Depois de atualizar, alguém do time liga o projeto uma vez e commita `ProjectSettings/HopperSettings.json`.

## [0.7.0] — 2026-11-12

### Added
- Mapa de clipes (Hopper de 12/nov): cada arquivo de animação chega com `animation.clips`
  [{ name, take, take_index, first_frame, last_frame, loop }] e o import cria **um clipe do importer por
  linha** — take (pelo nome, ou pela posição se o nome não bater), primeiro e último frame, loop e nome.
  Resolve o pacote (Locomotion com Idle, Walk e Run num FBX) e a ação cortada (Jump_In, Jump_Loop e
  Jump_Out do mesmo take). Os frames do Hopper (fps do projeto, contados do começo do take) são convertidos
  para o sample rate e o começo do take no FBX; `last_frame` -1 = até o fim do take.
- Mapa editado no Hopper sem versão nova: o manifesto guarda a assinatura do mapa (`clip_signature`), a
  janela mostra **Update** ("… clip map") e o overlay conta como "to update". O Update só reaplica o importer,
  sem baixar o arquivo de novo.

### Changed
- O loop de cada clipe vem do mapa: acabou o loop do primeiro take valendo para todos (v0.6).

### Notes
- Animações importadas pela v0.6 aparecem uma vez como **Update** ("clip map"): é o mapa sendo aplicado.
- Take do mapa que não existe no FBX vira aviso na linha de status, e o clipe é pulado.
- Servidor antigo (sem `clips`) importa como a v0.6.

## [0.6.0] — 2026-11-09

### Added
- Import de animação (projetos por Item, Hopper com o feed v0.6): os arquivos de rigging e animation
  chegam com `animation` { rig_type, fps, is_clip, loop, clip_name, frames } e o import é configurado
  sozinho — **Humanoid** ou **Generic** (do projeto, ou o do Item), clip com o nome da ação e o loop
  que o Hopper leu do arquivo. Clip Humanoid usa o avatar do SK_ do Item (importado antes, no mesmo
  Import); sem ele, cria o avatar do próprio clip e avisa na linha de status.
- As configurações vão para o importer e são salvas (`SaveAndReimport`): ficam no `.meta` e valem em
  qualquer máquina e em qualquer reimport.

### Notes
- Só vale para arquivo baixado de novo (Import/Update). Trocar o tipo de rig no Hopper não reconfigura
  um arquivo que já está em dia.
- Servidor antigo (sem o campo) importa como antes.

## [0.4.0] — 2026-09-28

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
