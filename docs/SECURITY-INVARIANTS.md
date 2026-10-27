# Security invariants and findings (Racks)

Registo de segurança do Racks. Cada invariante é uma regra que previne uma classe inteira.
Origem: sweep multi-agente read-only (5 lentes + síntese) em 2026-07-11.

Estado dos fixes: **Lotes A, B, C e D aplicados**. Todos os achados do sweep tratados.
Audit independente de 2026-10 (após o incidente de 2026-10-09: token GitHub roubado substituiu
assets de releases por malware): H1 fechado com assinatura própria, H2 sem UNC + allowlist, M4, L2 e
L6 corrigidos.

Modelo de ameaça: app desktop nativa (sem servidor/HTTP/backend). A fronteira de confiança é
outros processos locais (mesmo utilizador), o sistema de ficheiros, o caminho de rede até ao
GitHub, e um release comprometido. Muitos itens "same-user" não são fronteira de privilégio numa
máquina de um só utilizador; são robustez / cumprir a promessa "nada se perde numa rack".

---

## Invariantes (regras a manter)

- **INV-UPDATE-1** Nunca executar um binário descarregado sem verificar proveniência E integridade
  independentes do transporte: validar a URL (HTTPS, host do GitHub, repo esperado, nome de asset
  esperado) e verificar a assinatura Authenticode / hash publicado antes do `Process.Start`.
  *(revisão + teste)* ✅ Aplicado (2026-10) com uma assinatura própria ancorada FORA do GitHub:
  manifesto canónico `{product, version, fileName, sha256, size}` assinado com ECDSA P-256; chave
  pública compilada (`Util/UpdateTrust.cs`), privada só no Credential Manager do dono. O updater
  exige assinatura válida, versão assinada == tag do release e > versão atual, e tamanho/sha256
  dos bytes descarregados, verificados por um handle que nega escrita/apagamento e fica aberto até
  o instalador arrancar. Placeholder da chave ⇒ nenhum update. O updater lê a primeira página de
  `/releases` (até 30) e oferece o release assinado mais recente que verifica, para um release
  partido não esconder um bom; os toasts só mostram a versão validada, nunca texto da API, e um
  release que não verifica nunca é nomeado nem ligado (sem versão, sem link para a página de
  releases: só "instale apenas updates que o Racks verifica"). Uma build sem a chave não contacta o
  GitHub e só responde a uma verificação pedida pelo utilizador, com texto neutro.
  *(teste: `UpdateSignatureTests`, `UpdaterAssetTests`, `UpdaterReleaseScanTests`,
  `UpdaterMessageTests`; código
  partilhado com `tools/RacksSigner`)*
- **INV-IMPORT-1** Input importado é validado antes de virar estado persistido: allowlist de nomes
  de valor, tipos/ranges validados, `Folder`/`BackgroundImagePath` canonicalizados e a rejeitar
  UNC/sistema/traversal, e `rack.Name` só um segmento de chave seguro. *(teste)* ✅ Aplicado
  (2026-10, `RackLayoutIO.ParseAndValidate`): allowlist de nomes+tipos comparada sem distinguir
  maiúsculas (o registo também não distingue), nomes reescritos na grafia canónica, duas grafias do
  mesmo nome rejeitadas, nomes desconhecidos descartados, `MultiString` só para `LastAccessedFiles`/
  `CustomOrderFiles`; `Folder`/`BackgroundImagePath` só aceitam caminhos locais totalmente
  qualificados (`X:\...`): tudo o que começa por dois separadores (`\\host`, `//host`, `\\?\`,
  `\\.\`, `\\?\UNC\`, `\\host@SSL`) é rejeitado, tal como `C:x`, `\x`, `..` e `:` de ADS. O
  Uma pasta de rede que o utilizador escolhe na própria app continua permitida: a regra é só para
  ficheiros importados. O auto-route não revalida a pasta da rack, mas o destino passa sempre pelo
  `FileOperationBroker` (`PathPolicy`, pastas protegidas, nunca sobrescreve), ver INV-ROUTE-1.
  *(teste: `RackLayoutImportValidationTests`)*
- **INV-IMPORT-3** Valores de racks que o WPF transforma em recursos nunca podem nomear uma
  localização: `new FontFamily(@"\\host\share\#Fonte")` abre a share, o `ColorConverter`/
  `BrushConverter` aceita `ContextColor <uri> ...` e abre esse perfil ICC, e uma imagem de fundo
  UNC é aberta (até o `File.Exists`). Qualquer destes vaza o hash NTLM. *(teste)* ✅ Aplicado
  (2026-10, `Util/SafeWpfValues`): fonte só com nomes (`[\p{L}\p{N} ._'&-]`, até 8 separados por
  vírgula, sem `\ / : # %`), cor só `#hex`, nome ou `sc#` e que o `ColorConverter` aceite (o padrão
  corre primeiro e recusa `ContextColor` antes de qualquer parse), imagem de fundo só caminho local
  `X:\...`. Validado no import (tipos `FontFamily`/`Color` na allow-list), nos setters do
  `Instance` (cobre valores já gravados no registo, incluindo os defaults globais que o construtor
  `Instance(name, settingDefault)` lê com a persistência suspensa, o diálogo e as bindings XAML) e todos os
  `new FontFamily(...)` com dados de racks passam por `SafeWpfValues.CreateFontFamily` (fallback
  "Segoe UI"). *(teste: `SafeWpfValuesTests`, incl. guarda contra `new FontFamily(` fora da
  fábrica; `RackLayoutImportValidationTests`)*
- **INV-IMPORT-4** Um SVG mostrado como miniatura nunca resolve nada fora do ficheiro. O Svg 3.4.7
  resolve por omissão imagens e elementos externos (`ExternalType.Local | Remote`): um
  `<image xlink:href="http://host/x.png">`, `\\host\share\x.png` ou `<use xlink:href="file://host/...">`
  num `.svg` do Desktop faria o Racks ir à rede ao mostrar o ficheiro (beacon; hash NTLM em
  UNC/`file:`). *(teste)* ✅ Aplicado (2026-10, `Util/SafeSvg`): `ResolveExternalImages`,
  `ResolveExternalElements` e `ResolveExternalXmlEntites` a `None` e `DisableDtdProcessing`, no
  arranque (`App.OnStartup`) e outra vez antes de cada parse; todo o parse de SVG passa por
  `SafeSvg.Open`. *(teste: `SafeSvgTests`: API estática fixada por reflexão, imagem externa local
  desenhada só com a resolução ligada, guarda contra `SvgDocument.Open` fora do `SafeSvg`; sem rede)*
- **INV-IMPORT-2** Um replace destrutivo é atómico ou tem backup: validar e preparar tudo primeiro,
  só apagar quando o novo estado escreve limpo, e fazer rollback do snapshot em falha. *(teste)*
  ✅ Aplicado (Lote A #5).
- **INV-REGEX-1** Todo o regex compilado de input persistido/importado tem `MatchTimeout` (e degrada
  em vez de crashar/congelar). *(mecânica: existe um scanner grep "new Regex(" sem timeout; teste)*
  ✅ Aplicado (Lote A #3, `Util.SafeRegex` + belt global).
- **INV-DELETE-1** Gates de segurança falham fechados: para itens protegidos, allowlist de verbos
  seguros e tratar verbo irresolúvel como bloqueado; consultar VERBW e VERBA. *(revisão)* Parcial:
  VERBW+VERBA aplicado (Lote A #7); allowlist total NÃO aplicada (ver nota #7).
- **INV-DATALOSS-1** Nunca destruir a fonte por heurística de nome: verificar que a cópia terminou
  (comparar árvore/tamanho, ou mover via `IFileOperation`) antes de apagar; preferir Reciclagem a
  delete permanente para conteúdo de racks. *(teste)*
- **INV-PATH-1** Canonicalizar antes de comparar paths (via `GetFinalPathNameByHandle` /
  `File.ResolveLinkTarget`); recusar mover um path cujo final difere do literal, salvo mover o
  próprio reparse point não-recursivamente. *(teste)*
- **INV-DELETE-2** Todos os deletes recursivos de árvores de racks passam por `SafeDelete` como
  único ponto auditado; classificar e apagar contra o mesmo handle (`FILE_FLAG_OPEN_REPARSE_POINT`).
  *(revisão + mecânica: grep "Directory.Delete(" fora de SafeDelete)*
- **INV-EXEC-1** Executáveis em diretórios graváveis pelo utilizador têm nome aleatório e são
  verificados imediatamente antes de lançar. *(revisão)* Updater: pasta nova
  `%TEMP%\Racks-update-<guid>` com DACL só do utilizador, verificação pelo handle mantido aberto
  até ao `Process.Start` (*teste: `UpdaterTempDirTests`*).
- **INV-RELEASE-1** O CI nunca assina nem publica: só cria um release em rascunho. Actions fixadas
  por SHA completo, permissões por job (`build` lê, só `draft` escreve), `persist-credentials:
  false`, dados do evento só via `env`, ferramentas de build (Inno Setup) com versão e SHA-256
  fixos. Assinar e publicar é feito pelo dono com `tools/release-sign.ps1`. *(revisão:
  `docs/RELEASING.md`)* A máquina com a chave nunca compila nem corre código de uma working tree:
  o RacksSigner é compilado uma vez de um commit revisto (`tools/install-signer.ps1`, a partir do
  `git archive` desse commit) para `%LOCALAPPDATA%\RacksSigner\<commit>\`, com o SHA-256 de cada
  ficheiro (ocultos incluídos, `-Force`) em `trusted.json` (fora do repo). O próprio
  `install-signer.ps1` corre a partir da cópia extraída desse commit (`git show
  <sha>:tools/install-signer.ps1`), nunca da working tree, e recusa correr se o seu conteúdo não
  for igual ao do commit. Antes de confiar num commit sem tag assinada mostra o histórico e o diff,
  desde o signer instalado, de tudo o que decide o build: `tools/`, `UpdateManifest.cs`,
  `UpdateTrust.cs`, `Directory.Build.*`, `Directory.Packages.props`, `global.json`, `NuGet.config`
  e `.gitattributes`. O `release-sign.ps1` instalado só corre esse signer (e recusa ficheiros a
  mais, ocultos incluídos), exige uma tag anotada assinada com uma chave SSH própria (que não é
  credencial do GitHub, `allowed_signers` fora do repo) igual à do GitHub, mostra `git log`/`git
  diff --stat` desde a última tag assinada e pede confirmação escrita ANTES de assinar; depois
  assina, envia e publica de seguida, e se algo falhar ou a execução for interrompida (Ctrl+C,
  fechar a consola) o bloco `finally` apaga os ficheiros assinados do rascunho e do disco. A
  verificação da chave pública no CI é um teste (`ReleaseGateTests`), não uma procura de texto.
  *(teste: `ReleaseScriptTests`)*
- **INV-INSTANCE-1** Single-instance usa namespace `Local\` (ou DACL owner-only). *(mecânica: grep
  `Global\\`)* ✅ Aplicado (Lote A #12).
- **INV-PROC-1** Nunca passar strings externas por `cmd.exe /c` concatenado; usar `ArgumentList`
  ou a API nativa. *(mecânica: grep "cmd.exe" + interpolação)*
- **INV-UNINSTALL-1** O uninstall nunca apaga dados de utilizador. `Racks.exe --uninstall-cleanup`
  (chamado pelo `[Code]` do `installer/Racks.iss` em `usUninstall`, antes de o Inno apagar ficheiros)
  devolve ao Desktop tudo o que está em `RacksWorkspace` e `%AppData%\Racks\VirtualFrames`: só
  renames no mesmo volume, nunca sobrescreve (sufixo "(from Racks)"), junções/symlinks e nomes
  extra de hardlinks são desligados (os dados ficam no alvo), e o que não puder ser movido fica numa
  `RacksWorkspace` visível com nota. `[UninstallDelete]` só remove essas pastas com `dirifempty`.
  *(teste: `UninstallCleanupTests`; revisão: nenhum `filesandordirs` sobre pastas com dados)*
- **INV-REMOVE-1** Remover uma rack nunca apaga ficheiros de utilizador. A pasta de uma rack em
  `VirtualFrames` contém os ficheiros movidos pelo drop; ao remover, `UninstallCleanup.ReturnFolderToDesktop`
  devolve-os ao Desktop e a pasta só é apagada se ficar vazia; se algo não puder ser movido a rack é mantida.
  Apagamentos recursivos só existem no `SafeMove`. *(teste: `UninstallCleanupTests`, guard
  `Nothing_outside_SafeMove_deletes_folders_recursively`)*
- **INV-IDS-1** Identificadores persistidos são leaf names por contrato: no load, descartar entradas
  onde `Path.GetFileName(name) != name`; após cada `Path.Combine` com dados guardados, afirmar que o
  path resolvido fica sob a raiz esperada. *(teste)*
- **INV-FS-1** Toda a operação destrutiva sobre ficheiros do utilizador (mover, renomear, apagar,
  Reciclagem, criar pasta) passa por `Infrastructure/FileSystem/FileOperationBroker`: valida nomes e
  paths (`PathPolicy`), verifica pastas protegidas e aliases (via `SafeMove`/`SafeDelete`, que são
  `internal` e só o broker chama), confirma o resultado no disco e escreve um registo por chamada em
  `%AppData%\Racks\audit.log` (JSON, rodado, só local, escrita síncrona). Privacidade, sem exagero: só o
  prefixo exato da pasta do perfil é trocado por `%USERPROFILE%`; nomes de ficheiros e os restantes
  caminhos ficam no registo (não há campos de utilizador/máquina), por isso ler antes de partilhar.
  Falhas esperadas são um estado (`Rejected`/`Skipped`/`Failed`), nunca uma exceção. *(teste: `FileMutationGuardTests` falha se um ficheiro fora do gateway tiver mais chamadas
  `File.`/`Directory.` Move/Delete/Copy/CreateDirectory/SetAttributes/Replace/Write*/AppendAllText ou
  `.MoveTo(` do que `Guards/fs-allowlist.json` permite, e se algo fora do gateway referir
  `SafeMove.`/`SafeDelete.`; `FileOperationBrokerTests`, `FileAuditSinkTests`,
  `BrokerEmptyDirUnlinkShortcutTests`, `ProtectedAncestorTests`)* Na P2 tudo o que toca em dados do
  utilizador passou pelo broker: além de mover/renomear/apagar, o broker tem `DeleteEmptyDirectory` (uma
  pasta vazia, nunca recursivo, recusa pastas protegidas, ligações e pastas com qualquer coisa dentro),
  `Unlink` (só remove um *nome*: junção/symlink, ou um nome extra de hardlink cujo conteúdo continua
  acessível por outro nome; recusa pasta real, ficheiro com um só nome e reparse points que não são
  ligações, como placeholders do OneDrive) e `DeleteRacksShortcut` (só ficheiros simples `.lnk`/`.url` até 1 MiB,
  diretamente dentro da pasta indicada pelo chamador e que não seja protegida; não há prova de que o Racks
  criou o ficheiro, por isso o chamador passa a pasta de uma sandbox de rack). Migrados: o atalho órfão de uma rack
  virtual, a pasta de rack esvaziada no Desktop, o nome de hardlink do workspace (`WorkspaceReturn`), as
  pastas vazias do desfazer do Magic Organize, as junções do `RackMirror` e, no `UninstallCleanup`, os
  renames (`MoveSameVolume`), os desligamentos de junções/hardlinks (`Unlink`) e a remoção de pastas vazias
  (`DeleteEmptyDirectory`). A allowlist tem agora 9 ficheiros (32 chamadas, antes 12 ficheiros e 45), todos
  estado do próprio Racks (logs, pasta `RacksWorkspace` e `desktop.ini`, pasta-raiz das junções, sandboxes
  vazias em `VirtualFrames`, relatório/nota do uninstall, ficheiro temporário do updater, export do
  layout) ou uma cópia sem apagar (o `.url` do `ShortcutFactory`); só pode descer. Limites conhecidos do guard (não
  vê chamadas por variável como `info.Delete()`, `File.Create`/`FileStream`, chamadas Win32/shell,
  `Process.Start`, nem código em `tools/`) estão no cabeçalho do teste; o guard impede o desvio fácil,
  não substitui a revisão. O guard exige contagem *igual* à da allowlist (a mais ou a menos falha).
  Proteção por contenção (P2): `Move`, `MoveSameVolume`, `Rename`, `DeleteTree` e `DeleteToRecycleBin`
  recusam também uma origem que seja ANTECESSORA de uma pasta protegida (`%USERPROFILE%\AppData`,
  `C:\Users`, a raiz do OneDrive quando o Desktop/Documentos estão redirecionados para lá, a raiz de uma
  partilha UNC onde estão), com o texto "folder that contains a protected system folder (...)". A
  comparação é sobre caminhos canónicos com prefixo e barra final, por isso `C:\Users\a` não é antecessor de
  `C:\Users\ab`; pastas *dentro* de uma pasta protegida (um ficheiro no Desktop, `Documentos\Projetos`, o
  workspace, as sandboxes) continuam permitidas. Num `Rename` ou `Unlink` olha-se para o item em si, não para
  o alvo: cada pasta protegida é comparada na forma resolvida, na forma "pai resolvido + nome próprio" (o
  caminho de uma junção que realocou o Desktop/Documentos/AppData para outro disco) e na forma que o
  Windows devolve, e um nome 8.3 do item é expandido; por isso a junção que *é* a pasta protegida
  recusa-se, e uma ligação qualquer que apenas aponta para ela (numa rack) renomeia-se e desliga-se.
  Limites herdados do `SafeMove`: (1) a proteção por contenção só vê as pastas da lista protegida; uma pasta
  de utilizador fora dela (por exemplo uma pasta de projetos) continua a ser movível por desenho; (2) sobre SMB o
  `GetFinalPathNameByHandle` devolve a forma UNC, por isso `\\localhost\C$\Users\eu\Desktop` NÃO é
  reconhecido como o Desktop; o `PathPolicy` recusa por isso partilhas administrativas (`X$`, `ADMIN$`),
  com o custo de uma rack não poder mover ficheiros para uma partilha administrativa de outra máquina.
  Reciclagem: `DeleteToRecycleBin` falha fechado (não apaga nada) em caminhos UNC ou fora de discos fixos,
  porque o `SHFileOperation` com `FOF_ALLOWUNDO|FOF_NOCONFIRMATION|FOF_SILENT` apagaria definitivamente e em
  silêncio. Risco residual que não se consegue saber de antemão: num disco fixo com a Reciclagem desligada
  ou pequena demais para o item, o Windows apaga mesmo definitivamente e o resultado é `Succeeded`; por
  isso só se usa depois de a cópia estar confirmada (drag-out). Renomear um link (junção/symlink) renomeia
  o link, não segue o alvo (a pasta protegida real continua recusada).
- **INV-FS-2** Um nome escrito pelo utilizador (renomear, nome de pasta do Magic Organize) é um *leaf
  name*: `PathPolicy.ValidateLeafName` recusa vazio, `\` `/`, `..`, `:` (drive e data streams), caracteres
  inválidos e de controlo, ponto ou espaço no fim, mais de 255 caracteres e nomes de dispositivo
  (`CON`, `NUL`, `COM1`... com ou sem extensão). `Rename` valida o nome antes de qualquer `Path.Combine`
  e confirma que o alvo fica na mesma pasta, por isso um rename nunca move o item para outro sítio;
  `CreateDirectory` valida o leaf e exige que a pasta-mãe exista. Nomes derivados de dados (grupos do
  Magic Organize) passam por `PathPolicy.SanitizeLeafName`. Paths gerais têm de ser completos e recusam
  espaço de dispositivo (`\\.\`, `\\?\GLOBALROOT`), `..`, ADS e wildcards; UNC é permitido (uma rack pode
  apontar a uma partilha de rede), ao contrário dos layouts importados (INV-IMPORT-1).
  *(teste: `PathPolicyTests`, `FileOperationBrokerTests`: rename com `..\x`, `C:\x`, `a:b`, `CON` é
  recusado e nada aparece fora da pasta)*
- **INV-ROUTE-1** O auto-route (uma rack com regex apanha ficheiros novos do Desktop) só move entradas
  *elegíveis*, através do `FileOperationBroker` (motivo `auto-route`), e cada decisão fica auditada.
  `RoutingPolicy` recusa: `desktop.ini`, itens ocultos ou de sistema, nomes de ficheiros por acabar
  (`.crdownload`, `.part`, `.partial`, `.tmp`, `.download`, `.opdownload`, `~$*`; o `Renamed` para o nome
  final é que é encaminhado), junções e symlinks (e um reparse point cujo tipo não se consegue ler: falha
  fechado), nomes que não são um leaf name (`PathPolicy.ValidateLeafName`) e ficheiros só-online da cloud
  (atributos offline, recall-on-open ou recall-on-data-access: mover um para fora da pasta sincronizada
  descarregava-o ou apagava-o nos outros dispositivos). Um ficheiro da cloud totalmente local (reparse point
  sem flag de recall) É encaminhado e nunca é aberto para a sondagem de "em uso". Um ficheiro com um parceiro
  `<nome>.part`/`.crdownload`/... ao lado, ou ainda aberto por outro programa (`FileShare.None` falha), é
  reavaliado com esperas de 1+2+4+8+15 s e depois desistido com um `Skipped` auditado; nunca fica em ciclo nem
  é largado em silêncio. Uma entrada que desapareceu antes de ser julgada (nome temporário) não gera registo.
  A regra de cada rack passa por `SafeRegex` (timeout de 250 ms, regex compilado guardado por rack). O texto da
  regra é guardado tal como está (`Instance.AutoRouteRegex` aceita qualquer texto, também no registo e no
  import: uma regra guardada nunca se perde por uma verificação no arranque); uma regra inválida ou lenta
  (`SafeRegex.IsAcceptable`, ou timeout ao casar) torna a rack inativa para auto-route, com um registo por
  rack+padrão e não por ficheiro. Só o editor (`AutoRouteRuleDialog`) recusa guardar um padrão assim, com
  mensagem. O watcher tem buffer de 64 KB, só pede nomes (`FileName`) e, em `Error`, volta a criar-se e revê
  o Desktop uma vez (espera de 250 ms, a dobrar até 30 s), encaminhando só nomes que NÃO estavam no Desktop
  antes (um snapshot do arranque mais as chegadas): os ficheiros que o utilizador mantém lá nunca são varridos. Auditoria: um movimento que chega ao broker é o registo do broker (`Move`, motivo
  `auto-route`); o que se decide antes (recusa da política, ficheiro ainda em uso, regra que não corre) é um
  registo `AutoRoute` do serviço (também uma falha ao gravar a rack depois de mover); um ficheiro que nenhuma
  rack quer não gera registo.
  *(teste: `RoutingPolicyTests`, `AutoRouteServiceTests`, `DesktopRouterTests`, `AutoRouteRegexSetterTests`)*

---

## Achados

Gravidade / estado. `file:line` do sweep (podem ter drift de 1-2 linhas).

### HIGH

**H1 — Updater executa `assets[0]` do release sem verificação** `Racks/Util/Updater.cs` · ✅ CORRIGIDO PARCIAL (Lote B)
Agora: asset escolhido por nome (`Racks-Setup-*.exe`, não `assets[0]` cego), URL validada (HTTPS + host `github.com` + path `/duartelcunha/Racks/releases/download/`), tamanho verificado vs a API, e re-validação antes de descarregar. ~~Resíduo aceite: compromisso genuíno do repo/conta GitHub~~. Em 2026-10-09 isso aconteceu (token roubado substituiu assets de releases por malware). ✅ CORRIGIDO (2026-10): assinatura ECDSA P-256 de um manifesto canónico, com a chave privada fora do GitHub; ver INV-UPDATE-1 e `docs/RELEASING.md`. Resíduo: instalações ≤ 1.4.0 têm o updater antigo até atualizarem; a chave no Credential Manager é legível por qualquer processo do mesmo utilizador na máquina que assina; a assinatura só vale o que vale a revisão do dono (assinar sem rever um commit malicioso produz um instalador assinado), daí a tag assinada, o signer fixado e a confirmação depois da lista de alterações (QA 2026-10, F2/F3). → INV-UPDATE-1, INV-RELEASE-1.

**H2 — Import de layout JSON sem validação, persistido no registo** `Racks/Util/RackLayoutIO.cs` · ✅ CORRIGIDO (Lote A nome; Lote C valida `Folder`/`BackgroundImagePath`: rejeita traversal `..` e paths não-rooted, na passagem atómica antes de apagar). Regex do import já limitado por SafeRegex (#3). ~~Resíduo: sem allowlist total de nomes de valor~~ e ~~UNC permitido~~: um audit independente (2026-10) mostrou que um layout com `Folder` UNC (ou `folder` em minúsculas, que escapava ao check sensível a maiúsculas) fazia o `DesktopRouter`/`SafeMove` mover novos ficheiros do Desktop para uma share remota e vazava o hash NTLM. ✅ CORRIGIDO (2026-10): allowlist de nomes+tipos sem distinguir maiúsculas e só caminhos locais `X:\...`. → INV-IMPORT-1. A revisão QA seguinte mostrou o mesmo vazamento por `TitleFontFamily`/`ItemFontFamily` (`\\host\share\#Fonte`) e pelas cores (`ContextColor <uri>`), que eram escalares livres: ✅ CORRIGIDO → INV-IMPORT-3.

**H3 — Regex do utilizador sem `MatchTimeout` no UI thread (ReDoS)** `DesktopRouter.cs:73`, `RackWindow.xaml.cs`, `RackViewModel.cs` · ✅ CORRIGIDO (Lote A #3) → INV-REGEX-1.

**H4 — Drag-out apaga original do workspace por heurística de nome, em corrida com a cópia async do Explorer** `Racks/RackWindow.xaml.cs` · ✅ CORRIGIDO (Lote C). Agora só remove o original depois de `CopyLooksComplete` confirmar que a cópia no desktop está completa (compara tamanho/contagem recursiva), e vai para a **Reciclagem** (`SafeDelete.ToRecycleBin`), nunca delete permanente. Se não confirmar, mantém o original (duplicado temporário em vez de perda). → INV-DATALOSS-1.

### MEDIUM

**M1 — Import com `replaceExisting` apaga todas as racks antes de escrever, sem rollback** `RackLayoutIO.cs` · ✅ CORRIGIDO (Lote A #5, atómico + rollback) → INV-IMPORT-2.

**M2 — `SafeMove` compara paths como strings; contornável por 8.3, `\\?\`, UNC, junctions** `SafeMove.cs` · ✅ CORRIGIDO (Lote C). `Canonicalize` via `GetFinalPathNameByHandle` resolve 8.3/junctions/subst/`\\?\` antes de comparar, em `IsProtectedPath` e `IsAncestorOrEqual`. Testado: nome 8.3 canonicaliza para o mesmo path (bypass fechado), sem falso-positivo em paths normais. → INV-PATH-1.

**M4 — Workflow de release com action de terceiros por tag mutável e `contents: write`** `.github/workflows/release.yml` · ✅ CORRIGIDO (2026-10). `softprops/action-gh-release@v3` (tag mutável) corria com `contents: write` no job inteiro, o checkout persistia o token e o `SHA256SUMS` era gerado no mesmo job. Agora: actions fixadas por SHA, jobs `build` (read) e `draft` (write, recebe o artefacto por id e confirma o digest), `gh release create --draft`, nunca publica. → INV-RELEASE-1.

**M3 — Proteção de delete fail-open, só verbo "delete" ANSI** `ShellContextMenu.cs` · PARCIAL: VERBW+VERBA aplicado (Lote A #7). Allowlist total NÃO aplicada de propósito: bloquear todos os verbos não-allowlisted arriscava partir itens legítimos de terceiros (Open With, Send To, extensões) — regressão de QoL. Verbo irresolúvel continua a passar. → INV-DELETE-1 (parcial).

### LOW

**L1 — Installer no temp com nome previsível `%TEMP%\Racks.exe`** `Updater.cs` · ✅ CORRIGIDO (Lote B, nome aleatório `Racks-update-{guid}.exe` + verificação de tamanho) → INV-EXEC-1.
**L2 — Uninstaller corre cópia de `{tmp}\RacksFarewell` (TOCTOU same-user)** `installer/Racks.iss` · ✅ CORRIGIDO (2026-10). O risco real era o modo admin: `PrivilegesRequiredOverridesAllowed=dialog` permitia instalar "para todos", e o uninstall elevado corria binários de pastas graváveis pelo utilizador. Esse modo nunca foi suportado (README: "No admin rights needed"; registo em HKCU) e foi removido: o instalador é só per-user. Defesa extra: com `IsAdmin` o `[Code]` não copia nem corre a animação a partir de `{tmp}`. Sem elevação, `{tmp}` é do mesmo utilizador (sem fronteira de privilégio). Resíduo: quem já instalou em modo admin fica com essa instalação; o próximo setup instala per-user ao lado (desinstalar a antiga à mão).
**L6 — `choco install innosetup` sem versão no CI** · ✅ CORRIGIDO (2026-10). Inno Setup 6.7.1 descarregado do fornecedor com SHA-256 fixo e editor Authenticode "Pyrsys B.V." verificado. → INV-RELEASE-1.
**L3 — `SafeDelete` check-then-recurse TOCTOU em junctions** `SafeDelete.cs` · ✅ CORRIGIDO (Lote D). Reescrito com `DirectoryInfo`, classifica pelos atributos da própria enumeração (uma só operação) em vez de um stat separado. Testado: árvore com junction é apagada, o alvo do junction sobrevive (sem perda de dados).
**L4 — `CopyDirectory` cross-volume segue junctions (recursão/explosão)** `SafeMove.cs` · ✅ CORRIGIDO (Lote D). Salta reparse points na cópia + cap de profundidade (64). Deletes do `CopyThenDelete` roteados por `SafeDelete`.
**L5 — Mutex single-instance `Global\` (squatting DoS)** `App.xaml.cs` · ✅ CORRIGIDO (Lote A #12, `Local\`) → INV-INSTANCE-1.
**L7: Miniaturas SVG resolviam imagens e elementos externos** `RackWindow.Files.cs` (Svg 3.4.7) ·
✅ CORRIGIDO (2026-10, QA ronda 3). Um `.svg` com `<image href>` remoto ou UNC fazia o thumbnailer
contactar esse host. → INV-IMPORT-4.

### INFO

**I1 — `mklink` via `cmd.exe` com interpolação (latente)** `JunctionHelper.cs` · ✅ CORRIGIDO (Lote D). Usa `ProcessStartInfo.ArgumentList` (quoting per-argumento), à prova de injeção mesmo que um caller futuro passe input não-filesystem. → INV-PROC-1.
**I2 — `AssignedFiles` do HKCU em `Path.Combine` sem rejeitar `..`** `RackWindow.xaml.cs` · ✅ CORRIGIDO (Lote D). No uso, salta entradas onde `Path.GetFileName(name) != name`. → INV-IDS-1.
**I3 — Comentários dizem que `Directory.Delete(recursive)` segue junctions; no .NET 10 não segue** `JunctionHelper.cs` · ✅ CORRIGIDO (Lote D). Comentário corrigido; raw deletes de árvores de racks roteados por `SafeDelete` (RackWindow drag-out via Reciclagem, SafeMove via SafeDelete). → INV-DELETE-2.

---

## Verificação

Métodos de segurança exercitados contra o **`Racks.dll` real** (não mirrors), 12/12 pass:
SafeRegex (ReDoS limitado a ~250ms), SafeMove (recusa Desktop + o seu alias 8.3 canonicalizado;
move ficheiro normal), SafeDelete (apaga árvore com junction, alvo do junction sobrevive),
SafeDelete.ToRecycleBin (envia para a Reciclagem), RackLayoutIO.Import (rejeita nome/Folder com
traversal antes de escrever). Falta apenas verificação GUI-only (gesto de drag-out real,
delete-protection num menu de shell real), que exige conduzir a app à mão.

## Code-signing (o residuo do updater)

Sem certificado, o updater não verifica Authenticode. Opções concretas:
- **SignPath Foundation** (recomendado): OV **grátis** para projetos open-source. O Racks
  qualifica (MIT, repo público, já tem releases). Assinatura via CI (GitHub Actions), chave num
  HSM deles. Aplicar em signpath.org. Quando estiver ativo: adicionar o passo de assinatura no CI
  + a verificação Authenticode+publisher obrigatória no updater (INV-UPDATE-1 fica fechada).
- **Azure Artifact Signing** (ex-Trusted Signing): ~10 USD/mês, mas para **indivíduos** só EUA/Canadá;
  na UE só organizações. Um solo dev em PT precisaria de registar empresa. Menos indicado.

## Segredos / exposição no GitHub

Verificado em 2026-07-11: **zero segredos** em ficheiros tracked ou no histórico git. Endpoints só
GitHub API/repo e ko-fi. `.gitignore` endurecido (`.env`, `secrets.json`, `*.pfx/.snk/.pem/.key`,
`*.reg`, etc.). Removido `test/last-test-result.json` (artefacto gerado, sem PII).
