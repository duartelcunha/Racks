# Security invariants and findings (Racks)

Registo de segurança do Racks. Cada invariante é uma regra que previne uma classe inteira.
Origem: sweep multi-agente read-only (5 lentes + síntese) em 2026-07-11; reforçado pelo audit
independente de 2026-10 e pelo plano de hardening P0-P13 (ver `docs/HARDENING-ROADMAP.md`).

Estado: todos os achados do sweep (Lotes A a D) e do audit de 2026-10 (após o incidente de 2026-10-09:
token GitHub roubado substituiu assets de releases por malware) estão tratados: H1 fechado com
assinatura própria, H2 sem UNC + allowlist, M4, L2 e L6 corrigidos. O que continua **aberto ou aceite**
está todo em "Riscos aceites e abertos" (abaixo): nada vive só no roadmap.

Este documento descreve o código **tal como está no fim da P13**. As referências a classes e testes foram
conferidas contra o repositório; se mudares um nome, muda-o aqui (um `grep` pelo nome antigo chega).

## Índice

Estado: **Aplicado** = a regra vale no código e está fixada por testes; **Parcial** = vale, mas com uma
lacuna conhecida (ver o risco indicado); **Aberto** = não aplicado (nenhum neste momento).
"Guard" = teste em `test/Racks.Tests/Guards/` que falha se o código se desviar (ver "O que cada guard vê").

| ID | Regra numa linha | Código que a impõe | Testes que a fixam | Estado |
|---|---|---|---|---|
| INV-UPDATE-1 | Nada descarregado corre sem manifesto assinado + versão + URL + tamanho + SHA-256; falha sempre fechada | `UseCases/Updates/*`, `Infrastructure/Update/*`, `Util/UpdateManifest.cs`, `Util/UpdateTrust.cs` | `UpdateSignatureTests`, `UpdaterCheckBehaviorTests`, `UpdaterInstallBehaviorTests`, `InstallerExecutorTests`, `ReleaseCatalogClientTests`, `UpdateServiceTests`, `ReleaseGateTests` | Aplicado (R-19, R-20) |
| INV-RELEASE-1 | O CI nunca assina nem publica; o dono assina com um signer fixado | `.github/workflows/release.yml`, `tools/install-signer.ps1`, `tools/release-sign.ps1` | `ReleaseScriptTests`, `ReleaseGateTests` | Aplicado |
| INV-EXEC-1 | Executável em pasta gravável: pasta nova, nome aleatório, verificado pelo handle antes de lançar | `Infrastructure/Update/InstallerExecutor`, `UpdateCleanupService` | `UpdaterTempDirTests`, `UpdaterInstallBehaviorTests`, `InstallerExecutorTests` | Aplicado |
| INV-IMPORT-1 | Layout importado: allowlist de nomes, tipos, caminhos locais, nome de rack seguro | `UseCases/Layout/LayoutValidator` + `LayoutSecurityPolicy` (allowlist de `RackSettingsSchema`) | `RackLayoutImportValidationTests`, `LayoutValidatorTests` | Aplicado |
| INV-IMPORT-2 | Replace destrutivo é atómico: snapshot, escrita, verificação, rollback conferido, ficheiro de recuperação | `UseCases/Layout/LayoutImportExecutor`, `LayoutRecovery`, `Infrastructure/Layout/RecoveryFolder` | `LayoutImportExecutorTests`, `LayoutReviewFixTests`, `LayoutRegistryImportTests` | Aplicado (R-26) |
| INV-IMPORT-3 | Fontes, cores e imagens de fundo nunca nomeiam uma localização (hash NTLM) | `Util/SafeWpfValues` (import, setters de `Instance`, loader) | `SafeWpfValuesTests`, `RackLayoutImportValidationTests` | Aplicado |
| INV-IMPORT-4 | Um SVG nunca resolve nada fora do ficheiro | `Util/SafeSvg` (passo `ApplySafeSvg` do arranque + antes de cada parse) | `SafeSvgTests`, `AppBootstrapServiceTests` | Aplicado |
| INV-IMPORT-5 | Um layout tem tamanho e forma limitados; problema de segurança recusa o ficheiro inteiro | `UseCases/Layout/{LayoutParser,LayoutValidator,LayoutSecurityPolicy,LayoutLimits}`, `Infrastructure/Layout/LayoutFileWriter` | `LayoutParserTests`, `LayoutValidatorTests`, `LayoutExportTests`, `LayoutReviewFixTests` | Aplicado |
| INV-REGEX-1 | Todo o regex de input persistido/importado tem timeout e degrada | `Util/SafeRegex` (250 ms) + timeout por omissão do AppDomain no arranque | `SafeRegexTests`, `AppBootstrapServiceTests`, `AutoRouteRegexSetterTests` | Aplicado (sem guard: R-23) |
| INV-DELETE-1 | Gates de segurança do menu de shell falham fechados | `Util/ShellContextMenu` (VERBW + VERBA) | revisão (sem teste) | Parcial (R-12) |
| INV-DELETE-2 | Deletes recursivos de árvores só por `SafeDelete`, classificando pelo mesmo handle | `Util/SafeDelete` (via `FileOperationBroker`) | `FileMutationGuardTests`, `SafeDeleteAdversarialTests`, `UninstallCleanupTests` | Aplicado (R-22) |
| INV-DATALOSS-1 | Nunca destruir a fonte antes de a cópia estar confirmada; Reciclagem em vez de delete permanente | `RackWindow.DragDrop` (`CopyLooksComplete` + `DeleteToRecycleBin`), `SafeMove.CopyThenDelete` | `CopyFallbackCleanupTests`, `SafeMoveCopyFallbackTests`, `SafeMoveAdversarialTests` | Parcial (R-01, R-07, R-16) |
| INV-PATH-1 | Canonicalizar antes de comparar; recusar o que difere do literal | `Util/SafeMove.Canonicalize`, `Infrastructure/FileSystem/PathPolicy` | `ProtectedAncestorTests`, `ProtectedLinkRootTests`, `SafeMoveAdversarialTests`, `PathPolicyTests` | Aplicado (R-08, R-09) |
| INV-INSTANCE-1 | Single-instance usa o namespace `Local\` | `UseCases/Startup/SingleInstanceGuard.MutexName` | `AppBootstrapServiceTests` | Aplicado |
| INV-PROC-1 | Nunca strings externas por `cmd.exe /c` concatenado | `Util/JunctionHelper` (`ArgumentList`) | revisão (sem teste) | Aplicado (R-22) |
| INV-UNINSTALL-1 | O uninstall nunca apaga dados do utilizador | `Util/UninstallCleanup` (sempre pelo broker), `installer/Racks.iss` | `UninstallCleanupTests`, `UninstallCleanupAdversarialTests`, `UninstallCleanupAuditTests` | Aplicado (R-04) |
| INV-REMOVE-1 | Remover uma rack nunca apaga ficheiros do utilizador | `Rack/WorkspaceReturn`, `UninstallCleanup.ReturnFolderToDesktop`, `Rack/RackFolderRemoval` | `UninstallCleanupTests`, `WorkspaceReturnTests`, `RackFolderRemovalTests`, `RackDeletionTests` | Aplicado (R-02) |
| INV-IDS-1 | Identificadores persistidos são leaf names; o nome de uma rack é um segmento seguro | `PathPolicy.ValidateLeafName`, `RackSettingsSchema.IsValidRackName`, `RackSettingsValidator`, `Rack/WorkspaceReturn` | `AssignedFilesItemsTests`, `WorkspaceReturnAdversarialTests`, `RackSettingsValidationTests` | Aplicado (fechado na P11c, F3) |
| INV-FS-1 | Toda a operação destrutiva sobre ficheiros do utilizador passa pelo `FileOperationBroker`, é validada e auditada | `Infrastructure/FileSystem/*` sobre `Util/SafeMove` e `Util/SafeDelete` (`internal`) | `FileMutationGuardTests`, `FileOperationBrokerTests`, `BrokerEmptyDirUnlinkShortcutTests`, `ProtectedAncestorTests`, `FileAuditSinkTests` | Parcial (R-01, R-07, R-08, R-10, R-11, R-22) |
| INV-FS-2 | Um nome escrito pelo utilizador é um leaf name (nunca move o item) | `PathPolicy.ValidateLeafName/SanitizeLeafName`, `FileOperationBroker.Rename/CreateDirectory/Move` | `PathPolicyTests`, `FileOperationBrokerTests` | Aplicado |
| INV-ROUTE-1 | O auto-route só move entradas elegíveis, pelo broker, e audita cada decisão | `UseCases/AutoRoute/{RoutingPolicy,AutoRouteService}`, `Util/DesktopRouter` | `RoutingPolicyTests`, `AutoRouteServiceTests`, `DesktopRouterTests`, `AutoRouteRegexSetterTests` | Aplicado (R-24) |
| INV-REG-1 | Valores globais no registo só por `AppSettings` / `MigrationMarkers`; nunca se aplica lixo | `Infrastructure/Registry/{AppSettings,MigrationMarkers}` | `AppSettingsTests`, `MigrationMarkersTests`, `SettingsAuditRecordTests`, `RegistryAccessGuardTests` | Aplicado |
| INV-REG-2 | Valores por rack e estilos globais só pelo repositório, a partir de uma só tabela; valor inválido nunca é aplicado | `Infrastructure/Registry/{RackSettingsSchema,RackSettingsValidator,RackSettingsMapper,RackSettingsRepository}` | `RackSettingsGoldenTests`, `RackSettingsValidationTests`, `RackSettingsRepositoryTests`, `RackSettingsSchemaTests` | Aplicado (R-14, R-15) |

## O que cada guard vê (e o que não vê)

Quatro testes de `test/Racks.Tests/Guards/` fazem de cerca. Nenhum substitui a revisão de código: são scanners
de texto sobre `Racks/` (comentários removidos; strings **não** são apagadas, por isso um texto com o padrão
conta, falha fechado). Cada um tem o cabeçalho do teste com a lista completa; aqui está o que importa.

- **`FileMutationGuardTests`** (INV-FS-1). Cada ficheiro de `Racks/` tem de ter EXATAMENTE o número de mutações
  brutas de `Guards/fs-allowlist.json` (`File.`/`Directory.` Move, Delete, Copy, Replace, CreateDirectory,
  SetAttributes, Create*, Write*, Append*; `.MoveTo(`; `new FileInfo/DirectoryInfo(..).Delete/CopyTo`;
  `File.Open`/`new FileStream` com intenção de escrita). A mais ou a menos falha. Proíbe `SafeMove.` e
  `SafeDelete.` fora de `Infrastructure/FileSystem/` e proíbe `using static`/alias que escondam os nomes.
  **Não vê:** chamadas por variável (`info.Delete()`, `dirInfo.Create()`, `info.Attributes =`),
  `File.Open`/`FileStream` com o modo numa variável, Win32/COM (`CreateHardLinkW` em `HardlinkHelper`,
  `DeviceIoControl` em `JunctionHelper`, `IPersistFile.Save` em `ShellLinkHelper`, `SHFileOperation` em
  `SafeDelete`), processos (`mklink` via `cmd`), a pasta de download do updater
  (`new DirectoryInfo(dir).Create(security)`), `new DirectoryInfo(..).Attributes =` no `UninstallCleanup`,
  verbos do menu de shell (cortar/colar/apagar corridos pelo Explorer), reflexão e `tools/`. Também **não prova**
  que as entradas da allowlist são só estado do próprio Racks: isso está no campo `why` de cada uma e na revisão.
- **`PresentationBoundaryTests`** (apresentação não toca no shell). Zero referências, em `MainWindow*`,
  `RackWindow*`, `SettingsWindow*`, `RackSettingsDialog*`, `AboutWindow*`, `HelpWindow*`, `QuickFinderWindow*`,
  `App.xaml.cs`, `Views/`, `ViewModels/` e `Converters/`, a `NotifyShellMove`, `NotifyShellUpdateDir`,
  `SHChangeNotify`, `DesktopIconPositioner`, `DesktopIcons.` e `DesktopIconManager`. Mais: `MainWindow.xaml.cs`
  não pode ter código de ficheiros, registo, updater, hook, hotkey ou cursor, nem mais de um `DispatcherTimer`.
  **Não vê:** `[DllImport("shell32")]`/`SHFileOperation` novos na apresentação, COM de shell, `Process.Start`,
  reflexão, XAML, e código de apresentação fora da lista de ficheiros (uma pasta nova). O menu de contexto do
  shell, a geometria de janelas, `Process.Start` e o tray ficam na apresentação de propósito.
- **`RegistryAccessGuardTests`** (INV-REG-1). Cada ficheiro de apresentação tem exatamente o número de
  referências ao registo de `Guards/registry-presentation-allowlist.json` (hoje **vazio**: zero).
  **Não vê:** `reg.exe`/PowerShell lançados como processo (o "exportar definições" do `SettingsWindow` usa-o de
  propósito), reflexão, helpers fora dos ficheiros de apresentação (o executor de layouts usa `IRackRegistry`
  e não é apresentação), XAML e `tools/`.
- **`ArchitectureRatchetTests`** (catraias de tamanho). Só descem: `Guards/baseline.json` (valores na
  `docs/ARCHITECTURE.md`). **Não vê** qualidade nem acoplamento; só linhas, a maior parte de um ficheiro, usos
  de `MainWindow._controller` e literais em XAML.

Consequência: o que está escrito mais abaixo como "passa pelo broker" ou "só neste ponto" significa "o guard
impede o desvio fácil e a revisão trata do resto", nunca "provado por construção".

Modelo de ameaça: app desktop nativa (sem servidor/HTTP/backend). A fronteira de confiança é
outros processos locais (mesmo utilizador), o sistema de ficheiros, o caminho de rede até ao
GitHub, e um release comprometido. Muitos itens "same-user" não são fronteira de privilégio numa
máquina de um só utilizador; são robustez / cumprir a promessa "nada se perde numa rack".

---

## Invariantes (regras a manter)

- **INV-UPDATE-1** Nunca executar um binário descarregado sem verificar proveniência E integridade
  independentes do transporte. *(revisão + teste)* ✅ Aplicado (2026-10, reestruturado na P10) com uma
  assinatura própria ancorada FORA do GitHub: manifesto canónico `{product, version, fileName, sha256,
  size}` assinado com ECDSA P-256; chave pública compilada (`Util/UpdateTrust.cs`), privada só no
  Credential Manager do dono. **Falha sempre fechada**: nada descarregado corre a menos que, em
  conjunto, (1) a assinatura do manifesto verifique com a chave compilada, (2) o manifesto seja do
  release exato (versão assinada == tag) e mais novo que a versão em execução, (3) os URLs sejam
  HTTPS + `github.com` + `/duartelcunha/Racks/releases/download/` e acabem no nome exato do asset,
  (4) os bytes tenham o tamanho assinado e (5) o SHA-256 assinado. Placeholder da chave => nenhum
  update e nenhum pedido de rede. Quem faz o quê (`Racks/UseCases/Updates` = lógica,
  `Racks/Infrastructure/Update` = HTTP, ficheiros, processo, toasts):
  - `ReleaseManifestValidator` (puro): escolhe candidatos da primeira página de `/releases` (até 30;
    draft, prerelease e tags que não sejam `v<major>.<minor>.<patch>` ignorados), assets por nome
    exato (nunca `assets[0]`), allowlist de URL (`IsTrustedDownloadUrl`) e o primeiro candidato que
    verifica (um release partido não esconde um bom). Usa `UpdateVerifier` (`Util/UpdateManifest.cs`,
    código partilhado com `tools/RacksSigner`) para assinatura, tag, "mais novo", tamanho e hash.
  - `ReleaseCatalogClient`: só lê o URL do feed (`ReleasesApi`, a única definição; qualquer outro URL
    é recusado antes de qualquer pedido), a resposta tem tecto de 2 MiB, manifesto e `.sig` ficam em
    1024 bytes, e cada pedido tem timeout (feed 30 s, assets 20 s) e token de cancelamento. O fetch de
    assets pequenos volta a validar o URL contra a allowlist (defesa em profundidade: fora dela, null e
    nenhum pedido).
  - `InstallerExecutor`: descarrega para uma pasta nova, aleatória e só do utilizador
    (`FileMode.CreateNew`, `FileShare.None`), aborta ao primeiro byte além do tamanho assinado, cada
    leitura (e a espera pelos cabeçalhos) tem timeout (30 s), volta a recusar um URL fora da allowlist
    antes de criar a pasta, reabre o ficheiro só para leitura (nega escritores e apagadores),
    verifica tamanho e SHA-256 POR ESSE handle (`UpdateVerifier.VerifyInstaller`), lança o instalador
    enquanto o handle está aberto e só depois o liberta; qualquer falha apaga a pasta (a única que
    fica é a do instalador a correr, que o `UpdateCleanupService` remove mais tarde, nunca uma pasta
    com menos de 10 minutos nem com um ficheiro aberto; a pasta do update acabado de usar fica por isso
    10 minutos). Se o encerramento da app falhar DEPOIS de o instalador arrancar, conta como arranque
    (a pasta fica, sem toast de falha; fica no `audit.log`).
  - `UpdateService`: orquestra; só oferece o que verificou, uma instalação de cada vez, o oferecido
    (`_pending`) protegido por lock, gasto quando o instalador arranca e retirado por qualquer
    verificação que falhe (offline, feed inválido, URL recusado, nada mais novo): um toast antigo não
    instala uma oferta que a última verificação não confirmou; os toasts só mostram a versão
    do manifesto verificado (`UpdateMessages`), nunca texto da API, e um release que não verifica nunca
    é nomeado nem ligado. Uma build sem a chave não contacta o GitHub e só responde a uma verificação
    pedida pelo utilizador, com texto neutro. Toda a falha ou recusa fica em `audit.log`
    (`UpdateCheckFailed`, `UpdateInstallRefused`, `UpdateInstallFailed`, `UpdateInstallStarted`; URL sem
    query, sem segredos).
  - **Redirects (decisão consciente, não alterada):** o URL de um asset do GitHub responde com 302 para
    o armazenamento de assets noutro host, e o HttpClient segue-o. A allowlist de host/path vale para
    o URL tirado da API; para o host final a integridade vem da assinatura + tamanho + SHA-256, que
    um host qualquer não consegue forjar sem a chave privada. Impor o host final partiria as
    transferências legítimas e não acrescentava garantia.
  *(teste, por verificação: assinatura inválida/outra chave/DER/vazia/lixo/acima do tecto, manifesto
  não canónico, de outra versão ou repetido sob outra tag, URL fora de HTTPS/host/path:
  `UpdateSignatureTests`, `UpdaterCheckBehaviorTests`, `UpdaterAssetTests`, `UpdaterReleaseScanTests`,
  `UpdaterVersionTests`; tamanho (menor, maior, sem fim) e hash por handle aberto (escrita e
  apagamento bloqueados até ao arranque): `UpdaterInstallBehaviorTests`, `InstallerExecutorTests`;
  tectos, timeouts, cancelamento e URL do feed: `ReleaseCatalogClientTests`, `UpdateServiceTests`;
  redirect para outro host verificado pelo hash: `UpdaterInstallBehaviorTests`; textos, auditoria sem
  query, uma instalação de cada vez, `_pending` sob concorrência: `UpdateServiceTests`,
  `UpdaterMessageTests`; sem chave nenhum pedido: `UpdaterCheckBehaviorTests`; limpeza que ignora
  pastas recentes ou em uso: `UpdaterTempDirTests`; `ReleaseGateTests` falha o release sem chave real;
  código partilhado com `tools/RacksSigner`)*
- **INV-IMPORT-1** Input importado é validado antes de virar estado persistido: allowlist de nomes
  de valor, tipos/ranges validados, `Folder`/`BackgroundImagePath` canonicalizados e a rejeitar
  UNC/sistema/traversal, e `rack.Name` só um segmento de chave seguro. A allowlist deriva de
  `RackSettingsSchema` (INV-REG-2), a mesma tabela que o leitor usa. *(teste)* ✅ Aplicado
  (2026-10; desde a P9 em `UseCases/Layout/LayoutValidator` + `LayoutSecurityPolicy`, com `Util/RackLayoutIO` só como fachada fina): allowlist de nomes+tipos comparada sem distinguir
  maiúsculas (o registo também não distingue), nomes reescritos na grafia canónica, duas grafias do
  mesmo nome rejeitadas, nomes desconhecidos descartados, `MultiString` só para `LastAccessedFiles`/
  `CustomOrderFiles`; `Folder`/`BackgroundImagePath` só aceitam caminhos locais totalmente
  qualificados (`X:\...`): tudo o que começa por dois separadores (`\\host`, `//host`, `\\?\`,
  `\\.\`, `\\?\UNC\`, `\\host@SSL`) é rejeitado, tal como `C:x`, `\x`, `..` e `:` de ADS.
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
  "Segoe UI"). *(teste: `SafeWpfValuesTests`, incl. um scanner de texto contra `new FontFamily(` fora da
  fábrica (R-21); `RackLayoutImportValidationTests`)*
- **INV-IMPORT-4** Um SVG mostrado como miniatura nunca resolve nada fora do ficheiro. O Svg 3.4.7
  resolve por omissão imagens e elementos externos (`ExternalType.Local | Remote`): um
  `<image xlink:href="http://host/x.png">`, `\\host\share\x.png` ou `<use xlink:href="file://host/...">`
  num `.svg` do Desktop faria o Racks ir à rede ao mostrar o ficheiro (beacon; hash NTLM em
  UNC/`file:`). *(teste)* ✅ Aplicado (2026-10, `Util/SafeSvg`): `ResolveExternalImages`,
  `ResolveExternalElements` e `ResolveExternalXmlEntites` a `None` e `DisableDtdProcessing`, no
  arranque (passo `ApplySafeSvg` do `AppBootstrapService`, antes de `StartupUri`) e outra vez antes de cada parse;
  todo o parse de SVG passa por `SafeSvg.Open`. *(teste: `SafeSvgTests`: API estática fixada por reflexão,
  imagem externa local desenhada só com a resolução ligada, e um scanner de texto contra
  `SvgDocument.Open/FromSvg` fora do `SafeSvg` (R-21); a ordem do arranque: `AppBootstrapServiceTests`; sem rede)*
- **INV-IMPORT-2** Um replace destrutivo é atómico ou tem backup: validar e preparar tudo primeiro,
  só apagar quando o novo estado escreve limpo, e fazer rollback do snapshot em falha. *(teste)*
  ✅ Aplicado (Lote A #5) e, na P9, **testado de facto** (antes estava marcado como testado sem nenhum teste do
  `Import()`): `UseCases/Layout/LayoutImportExecutor` trabalha só sobre `IRackRegistry` (já não há `Registry.CurrentUser`
  no import). Ordem: snapshot das chaves que vão mudar (modo replace: todas; modo merge: as do ficheiro), com o tipo e os
  bytes exatos de cada valor (`ReadValuesExact`: REG_EXPAND_SZ não expandido); replace apaga cada chave; escreve cada rack;
  **verifica** lendo de volta o que escreveu. Qualquer falha em apagar/escrever/verificar (qualquer tipo de exceção:
  `IOException`, `UnauthorizedAccessException`, `SecurityException`...) faz rollback **preciso**: cada chave é registada
  *antes* de se tentar apagá-la ou escrevê-la e só essas são tocadas, por isso uma falha no primeiro `DeleteKey` não toca em
  nenhuma outra rack; uma chave alcançada que já é igual ao snapshot fica como está, as outras são apagadas e reescritas com
  os valores antigos e o mesmo tipo. O restauro é depois **conferido** (as chaves alcançadas, presentes ou ausentes como antes,
  mesmos valores, mesmos tipos). Um restauro que não completa **nunca é engolido** e o snapshot em memória, que passa a ser a
  única cópia das racks do utilizador, **é sempre gravado** como ficheiro de recuperação (`%AppData%\Racks\recovery\
  layout-<data>.json`, o mesmo formato de um export, atómico, as 5 mais recentes ficam; `UseCases/Layout/LayoutRecovery` +
  `Infrastructure/Layout/RecoveryFolder`): o resultado traz `RollbackFailed`, os detalhes e o caminho do ficheiro (ou porque
  não foi possível gravá-lo), escreve-se um registo `LayoutImportRollbackFailed` no `audit.log` com esse caminho e a interface
  diz ao utilizador onde está a cópia ("importe esse ficheiro para as ter de volta"). Importar o ficheiro de recuperação repõe
  as racks (só o que um layout pode ter: sem valores binários nem nomes que o Racks não conhece). Nada toca no registo antes de
  a validação terminar. Limite assumido: o snapshot e a conferência cobrem os **valores** das chaves de rack; subchaves
  aninhadas dentro de uma rack não são copiadas nem comparadas (a app não escreve nenhuma). *(teste:
  `LayoutImportExecutorTests` e `LayoutReviewFixTests` com um registo em memória que falha por ordem: sucesso em replace e em
  merge, falha no primeiro e no segundo delete, a meio da escrita de uma chave com cada tipo de exceção, na verificação, em
  merge, restauro que falha + ficheiro de recuperação que reimporta para o estado original, recuperação que também falha,
  ficheiro inválido deixa o registo intocado e nem o lê; `LayoutRegistryImportTests` sobre o registo descartável real:
  `Import()` completo, falha com rollback de todos os tipos REG_, round trip export→import byte a byte)*
- **INV-IMPORT-5** Um ficheiro de layout vem de fora (backup, partilhado por outra pessoa): tamanho e forma são limitados
  e o ficheiro é recusado inteiro ao primeiro problema de **segurança** (`UseCases/Layout/`: `LayoutParser` forma,
  `LayoutValidator` significado, `LayoutSecurityPolicy` política, `LayoutLimits` os números). Limites (`LayoutLimits`):
  ficheiro até **2 MiB** (o comprimento é verificado antes quando o ficheiro o diz, e a leitura pára assim que passa o limite,
  no máximo um bloco de 80 KB a mais: nunca `File.ReadAllText`); JSON até **8** níveis (um layout real tem 5; a varredura por
  tokens recusa antes de desserializar, por isso `[[[[...` não esgota a pilha); até **500** racks e **256** entradas em
  `Values` e em `MultiStringValues` de cada rack (contados logo a seguir ao parse, a memória fica limitada pelo teto de
  2 MiB do ficheiro); regex (`AutoRouteRegex`, `FileFilterRegex`, `FileFilterHideRegex`) até **1024** caracteres (o texto não
  é compilado no import: `SafeRegex` compila-o com timeout onde é usado, INV-REGEX-1); texto com NUL recusado (o registo
  cortava-o); uma propriedade repetida no mesmo objeto JSON (`"Folder"` duas vezes) recusa o ficheiro, em vez de a última
  ganhar em silêncio. `Version`: em falta lê-se como a atual, mais antiga lê-se, **mais nova é recusada** com mensagem
  ("atualize o Racks"), `< 1` é inválida. Nomes de racks **duplicados** comparam-se sem distinguir maiúsculas (as chaves do
  registo também não distinguem) e o valor `Name`, quando existe, tem de ser igual ao nome da rack (diferença só de maiúsculas
  é aceite). **Compatibilidade, decisão do lead:** os backups antigos têm de continuar a importar-se. Por isso, para os valores
  numéricos, interruptores, enums e **listas** (`AssignedFiles`, `LastAccessedFiles`, `CustomOrderFiles`,
  `ShowOnVirtualDesktops`) o que o `RackSettingsValidator` (o parser do load) não usaria **não recusa o ficheiro**: esse valor
  é largado como o load o largaria (aplica-se o valor por omissão; um número que o load corta, `IconSize`/`GridSize`, fica
  cortado; de uma lista ficam os itens bons), é escrito um registo `LayoutValueDropped` por valor (rack e campo; até 50, depois
  um resumo) e a mensagem final diz quais foram. Listas: até **10 000** itens, **1024** caracteres por item e **262 144** no
  total, senão a lista inteira é largada. Mantêm a **recusa do ficheiro inteiro** as regras de segurança: nomes/chaves, caminhos
  (UNC/dispositivo/ADS/traversal), fontes, cores, NUL e caracteres de controlo, tamanho, profundidade, nº de racks e de
  entradas, versão, duplicados, `Name` diferente da chave, formas JSON erradas (objeto onde devia haver escalar), regex acima do
  limite. Nenhuma entrada do registo é escrita até a validação inteira acabar. O **export** escreve em passo único
  (`Infrastructure/Layout/LayoutFileWriter`: ficheiro temporário oculto de nome curto e aleatório na mesma pasta, `Flush` a
  disco, um único `File.Move(..., overwrite: true)` por cima do destino; **não** se usa `File.Replace`, que pode falhar a meio
  com o destino já apagado; o temporário só é apagado se o destino estiver confirmadamente como estava, senão fica e o erro
  diz onde; o destino passa por `PathPolicy.ValidateFullPath`), por isso uma falha deixa o ficheiro anterior intacto, e antes
  de escrever o exporter corre os bytes pelo parser e pelo validador: um ficheiro que o próprio Racks recusaria (ou largaria
  valores) não é gravado, e a falha diz a rack e o campo. *(teste: `LayoutParserTests`, `LayoutValidatorTests`,
  `LayoutExportTests`, `LayoutReviewFixTests`, `RackLayoutImportValidationTests` — as regras de segurança antigas continuam as
  mesmas)*
- **INV-REGEX-1** Todo o regex compilado de input persistido/importado tem `MatchTimeout` (e degrada
  em vez de crashar/congelar). ✅ Aplicado (Lote A #3). Como: `Util/SafeRegex.TryCompile` (timeout de 250 ms;
  padrão inválido dá `null`) e `SafeRegex.IsMatch` (um timeout vira o valor de recurso que o chamador escolheu,
  nunca uma exceção); o auto-route, o filtro de ficheiros e o filtro de ocultar passam por aí. Como cinto,
  o arranque define o timeout por omissão do AppDomain (`REGEX_DEFAULT_MATCH_TIMEOUT`, passo
  `SetRegexDefaultTimeout` do `AppBootstrapService`), que cobre qualquer `Regex` sem timeout explícito (a
  procura rápida do rack escapa o texto do utilizador, só deixa `*`). *(teste: `SafeRegexTests`,
  `AppBootstrapServiceTests`, `AutoRouteRegexSetterTests`)* **Não há guard** que proíba um `new Regex(` novo
  sem timeout (a frase "scanner grep" que existia aqui nunca teve teste): ver R-23.
- **INV-DELETE-1** Gates de segurança falham fechados: para itens protegidos, allowlist de verbos
  seguros e tratar verbo irresolúvel como bloqueado; consultar VERBW e VERBA. *(revisão)* Parcial:
  VERBW+VERBA aplicado (Lote A #7; `Util/ShellContextMenu`); allowlist total NÃO aplicada (ver M3 e R-12).
- **INV-DATALOSS-1** Nunca destruir a fonte por heurística de nome: verificar que a cópia terminou
  (comparar árvore/tamanho, ou mover via `IFileOperation`) antes de apagar; preferir Reciclagem a
  delete permanente para conteúdo de racks. *(teste)* Parcial. No drag-out de uma rack do Desktop,
  `RackWindow.DragDrop` só recicla o original (`FileOperationBroker.DeleteToRecycleBin`) depois de
  `CopyLooksComplete` (tamanho/contagem) e, se não confirmar, mantém-no (R-16: esse método é privado e não tem
  teste direto). `SafeMove.CopyThenDelete` só apaga a fonte depois da cópia e, se a cópia falha, remove só o que
  essa chamada criou (P11b; `CopyFallbackCleanupTests`, `SafeMoveCopyFallbackTests`). Lacunas: R-01 (uma
  pasta noutro volume é recusada, não copiada), R-07 (numa rack virtual o original fica, duplicado, em vez de
  ser reciclado) e R-17 (Reciclagem desligada).
- **INV-PATH-1** Canonicalizar antes de comparar paths (via `GetFinalPathNameByHandle` /
  `File.ResolveLinkTarget`); recusar mover um path cujo final difere do literal, salvo mover o
  próprio reparse point não-recursivamente. *(teste: `SafeMoveAdversarialTests`, `ProtectedAncestorTests`,
  `ProtectedLinkRootTests`, `PathPolicyTests`)* Limites: R-08, R-09 e R-05.
- **INV-DELETE-2** Os deletes recursivos de árvores de racks vivem em `Util/SafeDelete` (`internal`), chamado
  pelo `FileOperationBroker` (e pelo `SafeMove` depois de uma cópia completa); classificar e apagar
  contra o mesmo handle (`FILE_FLAG_OPEN_REPARSE_POINT`). Não é um "ponto único" provado: o que se
  impõe é (1) `FileMutationGuardTests` falha se um ficheiro fora do gateway referir `SafeMove.`/`SafeDelete.` ou
  tiver mais `Directory.Delete(`/`File.Delete(` do que a allowlist (que só tem estado do próprio Racks) e (2)
  `UninstallCleanupTests.Nothing_outside_SafeMove_deletes_folders_recursively`, um scanner de texto que proíbe
  `SafeDelete.DeleteDirectoryRecursive` e `Directory.Delete(x, true)` fora de `SafeMove.cs`/`SafeDelete.cs`.
  O que nenhum vê: ver "O que cada guard vê" e R-22. *(revisão + os dois testes acima)*
- **INV-EXEC-1** Executáveis em diretórios graváveis pelo utilizador têm nome aleatório e são
  verificados imediatamente antes de lançar. *(revisão)* Updater: pasta nova
  `%TEMP%\Racks-update-<guid>` com DACL só do utilizador (`InstallerExecutor`), verificação pelo handle
  mantido aberto até ao arranque do instalador (*teste: `UpdaterTempDirTests`,
  `UpdaterInstallBehaviorTests`*). A limpeza (`UpdateCleanupService`) só apaga pastas com o nome exato,
  sem reparse points, com mais de 10 minutos e sem ficheiro aberto.
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
- **INV-INSTANCE-1** Single-instance usa namespace `Local\` (ou DACL owner-only). ✅ Aplicado (Lote A #12):
  `UseCases/Startup/SingleInstanceGuard.MutexName` é `Local\Racks-SingleInstance-2C9D`. *(teste:
  `AppBootstrapServiceTests` fixa o nome)*
- **INV-PROC-1** Nunca passar strings externas por `cmd.exe /c` concatenado; usar `ArgumentList`
  ou a API nativa. ✅ Aplicado (Lote D): o único `cmd.exe` é o `mklink /J` de `Util/JunctionHelper`, com
  `ProcessStartInfo.ArgumentList`; os restantes `Process.Start` abrem um caminho ou URL com `UseShellExecute`
  (sem `cmd`). Sem guard: revisão (R-22).
- **INV-UNINSTALL-1** O uninstall nunca apaga dados de utilizador. `Racks.exe --uninstall-cleanup`
  (chamado pelo `[Code]` do `installer/Racks.iss` em `usUninstall`, antes de o Inno apagar ficheiros)
  devolve ao Desktop tudo o que está em `RacksWorkspace` e `%AppData%\Racks\VirtualFrames`: só
  renames no mesmo volume, nunca sobrescreve (sufixo "(from Racks)"), junções/symlinks e nomes
  extra de hardlinks são desligados (os dados ficam no alvo), e o que não puder ser movido fica numa
  `RacksWorkspace` visível com nota. `[UninstallDelete]` só remove essas pastas com `dirifempty`.
  O `audit.log*` e o `crash.log*` são removidos; a pasta `%AppData%\Racks\recovery` (cópias de recuperação de
  layouts) **não** é removida pelo desinstalador, de propósito (R-04). *(teste: `UninstallCleanupTests`,
  `UninstallCleanupAdversarialTests`, `UninstallCleanupAuditTests`; revisão: nenhum `filesandordirs` sobre
  pastas com dados; a única é `{localappdata}\Racks`, que não guarda dados do utilizador)*
- **INV-REMOVE-1** Remover uma rack nunca apaga ficheiros de utilizador. A pasta de uma rack em
  `VirtualFrames` contém os ficheiros movidos pelo drop; ao remover, `UninstallCleanup.ReturnFolderToDesktop`
  devolve-os ao Desktop e a pasta só é apagada se ficar vazia; se algo não puder ser movido a rack é mantida.
  Apagamentos recursivos só existem em `SafeMove`/`SafeDelete` (INV-DELETE-2). A pasta de uma rack do Desktop que
  fica vazia só é apagada por `FileOperationBroker.DeleteEmptyDirectory`; se ficar alguma coisa, `Rack/RackFolderRemoval`
  avisa o utilizador uma vez. *(teste: `UninstallCleanupTests` (incl.
  `Nothing_outside_SafeMove_deletes_folders_recursively`), `WorkspaceReturnTests`, `RackFolderRemovalTests`,
  `RackDeletionTests`)*
- **INV-IDS-1** ✅ Fechado (P11c, F3). Identificadores persistidos são leaf names por contrato: no load, descartar entradas
  onde `Path.GetFileName(name) != name`; após cada `Path.Combine` com dados guardados, afirmar que o
  path resolvido fica sob a raiz esperada. (`AssignedFiles` desde a P11c: o load e o import de layouts descartam, item a item,
  os que não passam `PathPolicy.ValidateLeafName`, mantendo os bons e deixando o valor guardado como está; o uso, em
  `WorkspaceReturn` e no ciclo de remover rack, volta a exigir o mesmo, ver I2.) O nome de uma rack (que é uma chave do registo) é validado no load
  e em cada escrita ou remoção por `RackSettingsSchema.IsValidRackName` (um segmento, sem `\` nem `/`, nem `.`/`..`,
  até 255 caracteres): o mesmo critério do import (INV-REG-2). *(teste)* Lacuna fechada na P11c (F3): a verificação
  `Path.GetFileName(name) != name` aceitava `.`, e `WorkspaceReturn.ToDesktop(".")` movia a pasta `RacksWorkspace` inteira para o
  Desktop. Agora `.`, `..`, caminhos, drives, streams, nomes de dispositivo e ponto/espaço final são recusados no load
  (`SettingFallback`), no import (valor descartado) e no uso. *(teste: `AssignedFilesItemsTests`, `WorkspaceReturnAdversarialTests`)*
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
  (`DeleteEmptyDirectory`). A allowlist tem agora **11 ficheiros (36 chamadas)**; era 32 em 9 ficheiros até à P9, antes 12 ficheiros e 45
  (`DesktopIconManager` 7, `RackWindow.DragDrop` 2, `CrashLog` 3, `JunctionHelper` 5, `LayoutFileWriter` 3,
  `RecoveryFolder` 2, `RackMirror` 2, `ShortcutFactory` 1, `UninstallCleanup` 8, `InstallerExecutor` 1,
  `UpdateCleanupService` 2). Todas são, por revisão e pelo `why` de cada uma, estado do próprio Racks (logs,
  pasta `RacksWorkspace` e `desktop.ini`, pasta-raiz das junções, sandboxes vazias em `VirtualFrames`,
  relatório/nota do uninstall, pasta de download do updater, export do layout e pasta de recuperação) ou uma
  cópia sem apagar (o `.url` do `ShortcutFactory`); só pode descer. Única subida,
  justificada: na P9 o export do layout deixou de ser um `File.WriteAllText` (1 chamada, em `RackLayoutIO`) e passou a
  escrita atómica em `Infrastructure/Layout/LayoutFileWriter` (3 chamadas, cada uma explicada no `why` da allowlist: criar o
  temporário com `CreateNew`, um só `File.Move(..., overwrite: true)` para o destino que o utilizador escolheu, e apagar o
  próprio temporário) mais `Infrastructure/Layout/RecoveryFolder` (2: criar a pasta de recuperação e apagar as cópias de
  recuperação antigas do mesmo padrão). Limites conhecidos do guard (não
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
  Cópia + apagar (P11b): se a cópia falha a meio, `SafeMove.CopyThenDelete` remove SÓ o que essa chamada criou
  (cada ficheiro é criado com `CreateNew`, cada pasta com `CreateDirectoryW`, que falha se já existir; os caminhos
  criados são registados e removidos do mais novo para o mais antigo, sem apagar recursivamente), por isso um destino que
  apareça entre a verificação e a cópia nunca é fundido nem apagado. O nome final do destino de um `Move` passa por
  `PathPolicy.ValidateLeafName` antes de se tocar em nada (P11c, F2: um ficheiro movido para `dest\NUL` desaparecia no
  dispositivo NUL). Lacuna conhecida e fixada em teste (`Known_gap_...`, ver a P11 no roadmap): uma pasta noutro volume
  é recusada em vez de copiada (F1, R-01).
  Isto não é um "ponto único" absoluto: o broker é o único caminho para **mover, renomear, apagar e criar pastas
  com dados do utilizador** que o `FileMutationGuardTests` consegue ver; ligações duras, junções, atalhos, o
  resto do Win32/COM e o que o Explorer faz por si nos verbos do menu estão fora do broker e do guard (R-22).
  Parcial por R-01, R-07, R-08, R-10, R-11 e R-22.
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

- **INV-REG-1** Os valores globais do Racks no registo (`HKCU\SOFTWARE\Racks`: interruptores, marcadores de
  migração, `MainHwnd`) só se leem e escrevem por `Infrastructure/Registry` (`AppSettings`,
  `MigrationMarkers`); a apresentação não abre o registo (a lista `Guards/registry-presentation-allowlist.json` está vazia desde P6). Um interruptor é texto `True`/`False`
  (REG_SZ); um valor em falta dá o valor por omissão sem ruído (`IcePhysics` liga por omissão, o resto desligado);
  um valor de tipo errado, lixo ou um registo ilegível dá o valor por omissão e **um** registo `SettingFallback` no
  `audit.log` por nome (campo, valor em bruto encurtado, motivo, valor usado); nunca é aplicado. Uma escrita
  falhada devolve `false` e deixa um registo `SettingWriteFailed` (a interface segue como antes). Os
  formatos gravados não mudam (um build antigo lê o que este escreve): `MainHwnd` é REG_SZ decimal, não QWORD.
  O esquema por rack (`Instances\*`) é INV-REG-2. *(teste: `AppSettingsTests`, `MigrationMarkersTests`,
  `SettingsAuditRecordTests`, `RegistryAccessGuardTests`; raiz de registo descartável
  `Software\RacksTests-<guid>`, nunca a chave Run real)*


- **INV-REG-2** Os valores guardados por rack (`HKCU\SOFTWARE\Racks\Instances\<rack>`) e os 32 estilos
  globais por omissão (raiz `HKCU\SOFTWARE\Racks`) só se leem e escrevem por
  `Infrastructure/Registry/RackSettingsRepository`, a partir de **uma só tabela**, `RackSettingsSchema`
  (nome, tipo, limites, valor por omissão, se o import o aceita, se é estilo global). Essa tabela deriva a lista
  de importação (`LayoutSecurityPolicy`), o leitor, os escritores e a leitura dos estilos globais. No load cada valor
  passa por `RackSettingsValidator` **antes** de chegar ao modelo e um valor inválido **nunca é aplicado**: números
  em texto invariante primeiro e depois na cultura atual (o texto invariante, `0.5`, lê-se bem em qualquer cultura; o texto antigo de cultura, `0,5`, só se lê bem na cultura que o escreveu: em en-US `0,5` lê-se como 5, como no leitor antigo), NaN/Infinity/
  overflow recusados, limites por campo (`IconSize` 16-256 e `GridSize` 2-256 são cortados como os setters; os
  outros números fora do intervalo são recusados e vale o valor por omissão), booleanos só `True`/`False`, enums só
  membros definidos, fontes e cores só pelo `SafeWpfValues`, caminhos sem caracteres de controlo, nome da rack um
  único segmento seguro. Listas ficam com os itens bons (um item mau é descartado). Cada rack é lida no seu próprio
  `try`: **uma rack má nunca impede as outras**. Uma rack com `Name` inseguro é **saltada** e os seus dados ficam
  intactos no registo (nunca apagados); sem `Name` usa-se o nome da chave. **Carregar nunca escreve** no registo (o
  `Instance` é preenchido sob `SuspendPersistence`); as únicas escritas após um load são as duas migrações de uma
  vez. Cada valor recusado, cortado ou descartado é **um** registo `SettingFallback` no `audit.log` (rack, campo,
  valor em bruto até 200 caracteres, motivo, valor usado); um valor simplesmente em falta não gera registo. Uma
  escrita falhada devolve `false` e deixa um registo `SettingWriteFailed`; nada disto lança para a interface. Os tipos
  de valor gravados mantêm-se (inteiros REG_DWORD, interruptores `True`/`False`, listas como antes); os números
  decimais passam a ser gravados em texto invariante (um build antigo numa cultura com vírgula ou com ponto como separador de milhares lê mal esse texto: não recuar de versão). Cada achado regista-se uma vez por processo (rack, campo, valor); uma rack só com achados benignos (número cortado, `Name` em falta) é guardada uma vez em forma normal (`SettingNormalized`), nunca uma rack com valores recusados; um `Name` que difere da chave só em maiúsculas não é achado. *(teste: `RackSettingsGoldenTests`, `RackSettingsValidationTests`,
  `RackSettingsRepositoryTests`, `RackSettingsSchemaTests`; raiz descartável `Software\RacksTests-<guid>`, nunca
  a chave real)*
---

## Riscos aceites e abertos

Tudo o que ainda não está corrigido, ou que foi aceite de propósito, vive aqui (o roadmap só aponta para esta
secção). `R-NN` é o número que o índice e os invariantes citam. Gravidade: **Alta** = pode perder dados ou
executar código sem o utilizador querer; **Média** = função perdida ou risco real mas improvável, sem perda de
dados; **Baixa** = raro ou cosmético; **Info** = só convém saber. Nenhum é uma regressão: todos existiam antes do
hardening ou são o custo de uma escolha documentada. A coluna "Origem" diz onde está o detalhe e o teste que o fixa.

| ID | Risco | Gravidade | Recomendação | Origem / teste |
|---|---|---|---|---|
| R-01 | **F1.** Mover uma PASTA para outro volume nunca chega ao copy+delete: o `Directory.Move` do .NET lança um `IOException` cujo HResult não é `ERROR_NOT_SAME_DEVICE`, por isso o drop é recusado ("Failed to move", fonte intacta, nada criado). Ficheiros funcionam (o SO copia, sem verificação do Racks). O fallback de cópia só é alcançado por testes. | Média (função, sem perda) | Decidir pelas raízes dos volumes antes de mover (como o `SameVolume` do broker), usar `CopyThenDelete` + `CopyLooksComplete` e só depois apagar. Saber antes: a cópia salta ligações (o apontador perde-se, nunca o alvo) e pára aos 64 níveis (recusa inteira). Uma fase própria. | P11; `Known_gap_a_folder_move_to_another_volume_is_refused_whole_and_untouched` |
| R-02 | **F4 + identidade em `WorkspaceReturn`/`UninstallCleanup`.** Um ficheiro do workspace com outro nome de hardlink e o MESMO tamanho que um ficheiro não relacionado do Desktop com o mesmo nome é tomado pelo duplicado do Ctrl+drop e largado em vez de devolvido (nenhum byte perdido: os dados ficam no outro nome). "Contagem de ligações > 1" também assume que os dados sobrevivem noutro nome sem saber onde (pode ser `$Recycle.Bin`). | Baixa | Comparar identidade de ficheiro (número de série do volume + índice) em vez de nome+tamanho. | P2/P11; `Known_gap_a_hard_linked_file_with_the_same_length_but_other_data_is_taken_for_the_Desktop_file` |
| R-03 | **F5.** `DesktopRackClaims.IsGone` trata uma junção quebrada como pasta alcançável (`Directory.Exists` é true), por isso, num workspace que seja uma junção cujo alvo desapareceu, todos os nomes parecem "levados" e a reclamação cai num drag-out. | Baixa | Tratar junção quebrada como inexistente. | P11; `Known_gap_a_folder_that_is_a_dangling_junction_counts_as_reachable_so_its_names_are_gone` |
| R-04 | **F6.** Um segundo `--uninstall-cleanup` devolve ao Desktop a nota "ficheiros não devolvidos" escrita pela primeira, como se fosse um ficheiro do utilizador. Relacionado: a pasta `%AppData%\Racks\recovery` não é removida no uninstall (de propósito: pode ser a única cópia das racks). | Baixa (cosmético) | O passo 1 do `UninstallCleanup` passa a saltar também a nota; mencionar a pasta de recuperação na nota de uninstall. | P11 |
| R-05 | **F8.** `SafeMove.Canonicalize` de um destino que ainda não existe não resolve o pai através de uma junção, por isso "destino dentro da origem, por alias de junção" não é visto no preflight; o SO recusa o move e o broker diz `Failed` em vez de `Rejected`. Nada se move. | Info | Resolver o pai do destino antes de comparar. | P11 |
| R-06 | **Racks de raiz de disco não são guardadas.** Largar `D:\` (ou `D:`, ou uma raiz de partilha) numa rack vazia põe `Instance.Name = Path.GetFileName(folder)`, que é `""`; o nome é a chave do registo, por isso não se escreve nada, não se regista nada e a rack desaparece ao reiniciar. Duas pastas com a mesma última parte (`C:\a\Docs`, `D:\b\Docs`) dariam a mesma chave (não testado). | Média (a rack perde-se; ficheiros intactos) | Uma função só para o nome (largar, renomear, arranque): cai para a letra do disco ou o nome da partilha e torna-o único; ou recusar o drop com mensagem. | P6 e P11b; `DriveRootRackTests` |
| R-07 | **Rack virtual, cross-volume.** Depois de um drag-out com Move que o Explorer completa como cópia (outro disco), o original de uma rack virtual que é a única cópia já não é apagado (era um "atalho órfão"): fica na rack e o utilizador vê-o duas vezes. | Baixa (duplicado, sem perda) | O padrão da rack do Desktop: `CopyLooksComplete` e depois `DeleteToRecycleBin`. | P2 |
| R-08 | **Ligações Ctrl+largadas para a raiz do OneDrive, `C:\Users` ou `AppData`.** `Move`/`DeleteTree`/`DeleteToRecycleBin` ainda julgam uma junção pelo ALVO, por isso a regra do antecessor recusa mover ou apagar essa ligação (`Rename` e `Unlink` já são "link-aware"). | Baixa (função; recusa, não perde) | Tornar esses três "link-aware": é seguro para `DeleteTree`/Reciclagem e para um `Move` no mesmo volume. | P2 |
| R-09 | **Partilhas administrativas e SMB.** Sobre SMB o `GetFinalPathNameByHandle` devolve a forma UNC, por isso `\\localhost\C$\Users\eu\Desktop` não seria reconhecido como o Desktop; o `PathPolicy` recusa `X$`/`ADMIN$`. Custo aceite: uma rack não move ficheiros para uma partilha administrativa de outra máquina. | Info (aceite) | Nenhuma. | INV-FS-1 |
| R-10 | **`audit.log`.** A escrita é síncrona, na thread de quem chama (muitas vezes a UI); a rotação guarda `audit.log` e `audit.log.1` (cerca de 2 MB cada), por isso o histórico antigo desaparece sem aviso; os textos dos motivos só existem em inglês; os nomes de ficheiros ficam no registo (só o prefixo do perfil vira `%USERPROFILE%`). | Baixa | Fila em background para a escrita; política de retenção explícita (ou configurável); avisar o utilizador antes de partilhar o log. | P1/P2 |
| R-11 | **`Unlink` e `DeleteEmptyDirectory` agem por caminho depois das verificações.** A janela é de duas chamadas ao sistema e toda a falha dela é segura (ambos recusam pasta não vazia, pasta real ou ficheiro com um só nome). | Baixa | Apagar por handle, com desenho próprio. | P2; considerado e não feito na P11 |
| R-12 | **M3: verbos do menu de shell.** O gate de "Delete" lê VERBW e VERBA, mas um verbo que não se consiga resolver passa. A allowlist total partiria itens legítimos de terceiros (Abrir com, Enviar para, extensões). | Média | Manter; reavaliar com uma lista de verbos conhecidos como seguros se aparecer um caso real. | M3 abaixo; sem teste |
| R-13 | **Falha de arranque depois de o nome de instância único ser tomado.** Se um passo depois de `AcquireSingleInstance` lança, o handler do dispatcher marca-a como tratada, o WPF não abre `StartupUri` e, com `ShutdownMode=OnExplicitShutdown`, o processo fica vivo sem janela a segurar o mutex: todo o relançamento é recusado. Igual ao que era antes. | Média (disponibilidade) | Apanhar em `AppBootstrapService.Run`, registar e terminar (liberta o nome). | Revisão da P8 |
| R-14 | **As janelas numéricas de `RackSettingsSchema` não são decisões de produto.** Os limites que recusam um número fora do intervalo vêm do que a janela de definições deixa escolher; ninguém decidiu que um valor um pouco fora deve ser recusado em vez de cortado. | Baixa | O dono confirma cada janela (recusar ou cortar); mudar é uma linha da tabela, com teste dourado. | P6; `RackSettingsSchemaTests` |
| R-15 | **Descer de versão com texto decimal.** Os decimais passam a gravar-se em texto invariante (`123.5`). Um Racks antigo, num Windows com espaço como separador de milhares (português de Portugal, francês), não os lê e usa os valores por omissão; onde o separador de milhares é o ponto (alemão, espanhol, italiano, português do Brasil, neerlandês), lê `0.4` como 4 e `321.75` como 32175, e pode pôr racks fora do ecrã. | Média (só em downgrade) | Não recuar de versão (ou voltar a gravar as racks nessa versão). Está no CHANGELOG, em destaque. | P6; `RackSettingsGoldenTests` |
| R-16 | **O drag-out do Desktop confia em `CopyLooksComplete`** (`RackWindow.DragDrop`, método privado, tamanho/contagem) e não tem teste direto. | Baixa | Extrair para uma classe própria e testá-la como o `RackFolderRemoval`. | INV-DATALOSS-1 |
| R-17 | **Reciclagem desligada ou pequena.** Num disco fixo com a Reciclagem desligada, ou pequena demais para o item, o Windows apaga definitivamente e o resultado é `Succeeded`; não se sabe de antemão. Por isso só se recicla depois de a cópia estar confirmada. | Baixa (aceite) | Nenhuma além de manter a ordem "confirmar a cópia, depois reciclar". | INV-FS-1 |
| R-18 | **Redirects do download.** O URL do asset do GitHub responde com 302 para outro host; a allowlist vale para o URL da API, e o host final só é coberto pela assinatura + tamanho + SHA-256. Decisão consciente: impor o host final partiria transferências legítimas. | Info (aceite) | Nenhuma. | INV-UPDATE-1 |
| R-19 | **O instalador não tem assinatura Authenticode e o updater não verifica editor.** O Windows SmartScreen avisa na primeira execução. A proveniência vem só do manifesto assinado (ECDSA P-256) com a chave do dono. | Média | Quando houver certificado (ver "Code-signing"): assinar o instalador e acrescentar uma verificação Authenticode + editor no updater, como segunda camada. Não é preciso para a segurança atual. | INV-UPDATE-1 |
| R-20 | **Resíduos do updater.** Instalações ≤ 1.4.0 têm o updater antigo (sem assinaturas) até atualizarem; a chave privada no Credential Manager é legível por qualquer processo do mesmo utilizador na máquina que assina; a assinatura só vale o que vale a revisão do dono (assinar sem rever um commit malicioso produz um instalador assinado). | Média (aceite) | O processo de `docs/RELEASING.md` (tag assinada, signer fixado, confirmação depois da lista de alterações); manter a máquina de assinatura limpa. | H1; QA 2026-10 F2/F3 |
| R-21 | **Testes que ainda leem texto de código-fonte** (scanners, não testes de comportamento): `SafeSvgTests` (contra `SvgDocument.Open/FromSvg`), `SafeWpfValuesTests` (contra `new FontFamily(`), `UninstallCleanupTests.Nothing_outside_SafeMove_deletes_folders_recursively`, os quatro guards e `ReleaseScriptTests` (docs/scripts). São guardas de bordo, não substituem comportamento; contornam-se com um nome diferente. | Baixa | Manter só como guardas; qualquer regra nova nasce como teste de comportamento. | CONTRIBUTING |
| R-22 | **O que fica fora do broker e dos guards:** Win32/COM (`HardlinkHelper`, `JunctionHelper`/`mklink`, `ShellLinkHelper`, `SHFileOperation` em `SafeDelete`), os verbos do menu de shell corridos pelo Explorer, `Process.Start`, e o código de `tools/`. Foram revistos à mão; um novo de qualquer destes tipos tem de ser revisto à mão. | Média (processo) | Revisão obrigatória de qualquer `[DllImport]`, COM ou `Process` novo; ver CONTRIBUTING. | Cabeçalhos dos guards |
| R-23 | **Sem guard contra `new Regex(` sem timeout.** O timeout por omissão do AppDomain apanha o que escapar, mas a regra INV-REGEX-1 depende da revisão. | Baixa | Um guard com contagem exata, como o `FileMutationGuardTests`, se aparecer um caso. | INV-REGEX-1 |
| R-24 | **Auto-route, o que não faz de propósito:** sem varrimento inicial do Desktop ao arrancar (só depois de um `Error` do watcher), o watcher ignora pastas (filtro `FileName`), uma cópia grande que demore mais de cerca de 30 s fica no Desktop com um registo, ficheiros só-online da cloud são deixados, e a recusa do editor de regras não mostra mensagem (a regra anterior fica). | Baixa | Aceitar; reavaliar se aparecerem queixas. | P4; INV-ROUTE-1 |
| R-25 | **Manias do P7 mantidas (não se corrigem num refactor):** a segunda mensagem de ecrã testa `0x001C` (que é `WM_ACTIVATEAPP`, não `0x001A`); o Magic Organize pergunta `Directory.Exists(source)` DEPOIS de mover, por isso uma pasta movida é anunciada ao shell como ficheiro; sem Explorer o hit-test do desktop compara 0 com 0 e passa; os handlers dos interruptores do tray correm duas vezes por mensagem `WM_RACKS_TOGGLE_*` (idempotentes). | Baixa | Corrigir uma a uma, cada uma com o seu teste. | P7; `TrayToggleServiceTests` |
| R-26 | **Subchaves dentro de uma rack no registo** não são copiadas pelo snapshot do import nem comparadas na conferência do restauro (a app não escreve nenhuma). | Baixa | Nenhuma, a não ser que a app passe a escrever subchaves. | INV-IMPORT-2 |
| R-27 | **Dívida de arquitetura, não de segurança:** `MainWindow._controller` continua um `static` público (20 usos; `InstanceController.RefreshMirror` é o único fora da apresentação) e `RackWindow*` (cerca de 5900 linhas em partials) ainda é grande. As catraias impedem que cresçam. | Info | Passar dependências por construtor, rack a rack, descendo `controllerUsages`. | `ArchitectureRatchetTests` |
| R-28 | **Verificação manual pendente.** Nas fases P5 a P12 ninguém correu a app real, `test/Test-Racks.ps1` nem `tools/dev-tools.ps1 e2e` (matam o Racks em execução e mexem no Desktop real); o arranque, o tray, os hotkeys, o hook do rato, as caixas de diálogo e o instalador só estão cobertos pela lógica por trás deles. | Média (processo) | A lista de verificação manual do dono em `docs/HARDENING-ROADMAP.md`, antes de fundir a cadeia de PRs. | Roadmap |

## Achados

Gravidade / estado. `file:line` do sweep (podem ter drift de 1-2 linhas).

### HIGH

**H1 — Updater executa `assets[0]` do release sem verificação** `Racks/Util/Updater.cs` (ficheiro removido na P10; hoje `UseCases/Updates/*` e `Infrastructure/Update/*`) · ✅ CORRIGIDO (Lote B, e fechado em 2026-10 com assinatura própria)
Agora: asset escolhido por nome (`Racks-Setup-*.exe`, não `assets[0]` cego), URL validada (HTTPS + host `github.com` + path `/duartelcunha/Racks/releases/download/`), tamanho verificado vs a API, e re-validação antes de descarregar. ~~Resíduo aceite: compromisso genuíno do repo/conta GitHub~~. Em 2026-10-09 isso aconteceu (token roubado substituiu assets de releases por malware). ✅ CORRIGIDO (2026-10): assinatura ECDSA P-256 de um manifesto canónico, com a chave privada fora do GitHub; ver INV-UPDATE-1 e `docs/RELEASING.md`. Resíduo: instalações ≤ 1.4.0 têm o updater antigo até atualizarem; a chave no Credential Manager é legível por qualquer processo do mesmo utilizador na máquina que assina; a assinatura só vale o que vale a revisão do dono (assinar sem rever um commit malicioso produz um instalador assinado), daí a tag assinada, o signer fixado e a confirmação depois da lista de alterações (QA 2026-10, F2/F3). → INV-UPDATE-1, INV-RELEASE-1.

**H2 — Import de layout JSON sem validação, persistido no registo** `Racks/Util/RackLayoutIO.cs` (hoje uma fachada fina; a lógica está em `UseCases/Layout/*`, P9) · ✅ CORRIGIDO (Lote A nome; Lote C valida `Folder`/`BackgroundImagePath`: rejeita traversal `..` e paths não-rooted, na passagem atómica antes de apagar). Regex do import já limitado por SafeRegex (#3). ~~Resíduo: sem allowlist total de nomes de valor~~ e ~~UNC permitido~~: um audit independente (2026-10) mostrou que um layout com `Folder` UNC (ou `folder` em minúsculas, que escapava ao check sensível a maiúsculas) fazia o `DesktopRouter`/`SafeMove` mover novos ficheiros do Desktop para uma share remota e vazava o hash NTLM. ✅ CORRIGIDO (2026-10): allowlist de nomes+tipos sem distinguir maiúsculas e só caminhos locais `X:\...`. → INV-IMPORT-1. A revisão QA seguinte mostrou o mesmo vazamento por `TitleFontFamily`/`ItemFontFamily` (`\\host\share\#Fonte`) e pelas cores (`ContextColor <uri>`), que eram escalares livres: ✅ CORRIGIDO → INV-IMPORT-3.

**H3 — Regex do utilizador sem `MatchTimeout` no UI thread (ReDoS)** `DesktopRouter.cs:73`, `RackWindow.xaml.cs`, `RackViewModel.cs` (hoje `Util/SafeRegex`, `UseCases/AutoRoute`, `RackWindow.Files.cs`) · ✅ CORRIGIDO (Lote A #3) → INV-REGEX-1.

**H4 — Drag-out apaga original do workspace por heurística de nome, em corrida com a cópia async do Explorer** `Racks/RackWindow.xaml.cs` (hoje `RackWindow.DragDrop.cs`) · ✅ CORRIGIDO (Lote C). Agora só remove o original depois de `CopyLooksComplete` confirmar que a cópia no desktop está completa (compara tamanho/contagem recursiva), e vai para a **Reciclagem** (`FileOperationBroker.DeleteToRecycleBin`), nunca delete permanente. Se não confirmar, mantém o original (duplicado temporário em vez de perda). → INV-DATALOSS-1.

### MEDIUM

**M1 — Import com `replaceExisting` apaga todas as racks antes de escrever, sem rollback** `RackLayoutIO.cs` (hoje `LayoutImportExecutor`) · ✅ CORRIGIDO (Lote A #5, atómico + rollback) → INV-IMPORT-2.

**M2 — `SafeMove` compara paths como strings; contornável por 8.3, `\\?\`, UNC, junctions** `SafeMove.cs` · ✅ CORRIGIDO (Lote C). `Canonicalize` via `GetFinalPathNameByHandle` resolve 8.3/junctions/subst/`\\?\` antes de comparar, em `IsProtectedPath` e `IsAncestorOrEqual`. Testado: nome 8.3 canonicaliza para o mesmo path (bypass fechado), sem falso-positivo em paths normais. → INV-PATH-1.

**M4 — Workflow de release com action de terceiros por tag mutável e `contents: write`** `.github/workflows/release.yml` · ✅ CORRIGIDO (2026-10). `softprops/action-gh-release@v3` (tag mutável) corria com `contents: write` no job inteiro, o checkout persistia o token e o `SHA256SUMS` era gerado no mesmo job. Agora: actions fixadas por SHA, jobs `build` (read) e `draft` (write, recebe o artefacto por id e confirma o digest), `gh release create --draft`, nunca publica. → INV-RELEASE-1.

**M3 — Proteção de delete fail-open, só verbo "delete" ANSI** `ShellContextMenu.cs` · PARCIAL: VERBW+VERBA aplicado (Lote A #7). Allowlist total NÃO aplicada de propósito: bloquear todos os verbos não-allowlisted arriscava partir itens legítimos de terceiros (Open With, Send To, extensões) — regressão de QoL. Verbo irresolúvel continua a passar. → INV-DELETE-1 (parcial).

### LOW

**L1 — Installer no temp com nome previsível `%TEMP%\Racks.exe`** `Updater.cs` (hoje `InstallerExecutor`) · ✅ CORRIGIDO (Lote B, depois pasta nova e privada `%TEMP%\Racks-update-<guid>` + verificação de tamanho e SHA-256 pelo handle) → INV-EXEC-1.
**L2 — Uninstaller corre cópia de `{tmp}\RacksFarewell` (TOCTOU same-user)** `installer/Racks.iss` · ✅ CORRIGIDO (2026-10). O risco real era o modo admin: `PrivilegesRequiredOverridesAllowed=dialog` permitia instalar "para todos", e o uninstall elevado corria binários de pastas graváveis pelo utilizador. Esse modo nunca foi suportado (README: "No admin rights needed"; registo em HKCU) e foi removido: o instalador é só per-user. Defesa extra: com `IsAdmin` o `[Code]` não copia nem corre a animação a partir de `{tmp}`. Sem elevação, `{tmp}` é do mesmo utilizador (sem fronteira de privilégio). Resíduo: quem já instalou em modo admin fica com essa instalação; o próximo setup instala per-user ao lado (desinstalar a antiga à mão).
**L6 — `choco install innosetup` sem versão no CI** · ✅ CORRIGIDO (2026-10). Inno Setup 6.7.1 descarregado do fornecedor com SHA-256 fixo e editor Authenticode "Pyrsys B.V." verificado. → INV-RELEASE-1.
**L3 — `SafeDelete` check-then-recurse TOCTOU em junctions** `SafeDelete.cs` · ✅ CORRIGIDO (Lote D). Reescrito com `DirectoryInfo`, classifica pelos atributos da própria enumeração (uma só operação) em vez de um stat separado. Testado: árvore com junction é apagada, o alvo do junction sobrevive (sem perda de dados).
**L4 — `CopyDirectory` cross-volume segue junctions (recursão/explosão)** `SafeMove.cs` · ✅ CORRIGIDO (Lote D). Salta reparse points na cópia + cap de profundidade (64). Deletes do `CopyThenDelete` roteados por `SafeDelete`.
**L5 — Mutex single-instance `Global\` (squatting DoS)** `App.xaml.cs` (hoje `UseCases/Startup/SingleInstanceGuard`) · ✅ CORRIGIDO (Lote A #12, `Local\`) → INV-INSTANCE-1.
**L7: Miniaturas SVG resolviam imagens e elementos externos** `RackWindow.Files.cs` (Svg 3.4.7) ·
✅ CORRIGIDO (2026-10, QA ronda 3). Um `.svg` com `<image href>` remoto ou UNC fazia o thumbnailer
contactar esse host. → INV-IMPORT-4.

### INFO

**I1 — `mklink` via `cmd.exe` com interpolação (latente)** `JunctionHelper.cs` · ✅ CORRIGIDO (Lote D). Usa `ProcessStartInfo.ArgumentList` (quoting per-argumento), à prova de injeção mesmo que um caller futuro passe input não-filesystem. → INV-PROC-1.
**I2 — `AssignedFiles` do HKCU em `Path.Combine` sem rejeitar `..`** `RackWindow.xaml.cs` (hoje `Rack/WorkspaceReturn` e `RackWindow.Menu.cs`) · ✅ CORRIGIDO (Lote D) e fechado de vez na P11c (a verificação de então, `Path.GetFileName(name) != name`, deixava passar `.`; agora `PathPolicy.ValidateLeafName`). → INV-IDS-1.
**I3 — Comentários dizem que `Directory.Delete(recursive)` segue junctions; no .NET 10 não segue** `JunctionHelper.cs` · ✅ CORRIGIDO (Lote D). Comentário corrigido; raw deletes de árvores de racks roteados por `SafeDelete` (RackWindow drag-out via Reciclagem, SafeMove via SafeDelete). → INV-DELETE-2.

---

## Verificação

Verificação manual de 2026-07 (histórica, antes da P0): métodos de segurança exercitados contra o **`Racks.dll` real** (não mirrors), 12/12 pass. Desde então a verificação é a suite de testes (`dotnet test Racks.sln -c Release`: 1868 passam, 6 saltam, 0 falham no fim da P13):
SafeRegex (ReDoS limitado a ~250ms), SafeMove (recusa Desktop + o seu alias 8.3 canonicalizado;
move ficheiro normal), SafeDelete (apaga árvore com junction, alvo do junction sobrevive),
SafeDelete.ToRecycleBin (envia para a Reciclagem; hoje `FileOperationBroker.DeleteToRecycleBin`), o import de layouts (`RackLayoutIO.Import` na altura; hoje `LayoutService.ImportFile`) (rejeita nome/Folder com
traversal antes de escrever). Falta apenas verificação GUI-only (gesto de drag-out real,
delete-protection num menu de shell real), que exige conduzir a app à mão.

## Code-signing (duas coisas diferentes)

1. **O que o código faz hoje (assinatura do manifesto).** O updater verifica uma assinatura ECDSA P-256 sobre um
   manifesto canónico `{product, version, fileName, sha256, size}`, com a chave pública compilada em
   `Util/UpdateTrust.cs`; a chave privada está só no Credential Manager do dono. Em cima disso confere versão,
   URL, tamanho e SHA-256 (INV-UPDATE-1). É isto que garante a proveniência e a integridade do instalador, e
   falha sempre fechada. Está fechado e testado.
2. **O que o código NÃO faz (Authenticode do instalador).** O instalador `Racks-Setup-<v>.exe` não está
   assinado com um certificado de editor, e nada no updater (nem no instalador) chama `WinVerifyTrust`,
   `Get-AuthenticodeSignature` ou compara um editor: a única verificação Authenticode do repositório é a do
   Inno Setup no CI (`release.yml`, editor "Pyrsys B.V."). Consequência: o SmartScreen avisa na primeira
   execução ("Windows protected your PC") e o utilizador que descarrega o instalador à mão só tem o
   `SHA256SUMS.txt` para conferir. Está registado como R-19.

Authenticode seria uma segunda camada (sobretudo SmartScreen), não uma correção; INV-UPDATE-1 está fechada sem ela.
Opções concretas, **nenhuma decidida**:
- **SignPath Foundation** (recomendado): OV **grátis** para projetos open-source. O Racks
  qualifica (MIT, repo público, já tem releases). Assinatura via CI (GitHub Actions), chave num
  HSM deles. Aplicar em signpath.org. Quando estiver ativo: adicionar o passo de assinatura no CI
  + uma verificação Authenticode+publisher adicional no updater (que teria de entrar no `UpdateVerifier`,
  e por isso em `tools/RacksSigner`, que partilha esse código).
- **Azure Artifact Signing** (ex-Trusted Signing): ~10 USD/mês, mas para **indivíduos** só EUA/Canadá;
  na UE só organizações. Um solo dev em PT precisaria de registar empresa. Menos indicado.

## Segredos / exposição no GitHub

Verificado em 2026-07-11: **zero segredos** em ficheiros tracked ou no histórico git. Endpoints só
GitHub API/repo e ko-fi. `.gitignore` endurecido (`.env`, `secrets.json`, `*.pfx/.snk/.pem/.key`,
`*.reg`, etc.). Removido `test/last-test-result.json` (artefacto gerado, sem PII).
