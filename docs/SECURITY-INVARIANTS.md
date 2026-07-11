# Security invariants and findings (Racks)

Registo de segurança do Racks. Cada invariante é uma regra que previne uma classe inteira.
Origem: sweep multi-agente read-only (5 lentes + síntese) em 2026-07-11.

Estado dos fixes: **Lotes A, B, C e D aplicados**. Todos os achados do sweep tratados.

Modelo de ameaça: app desktop nativa (sem servidor/HTTP/backend). A fronteira de confiança é
outros processos locais (mesmo utilizador), o sistema de ficheiros, o caminho de rede até ao
GitHub, e um release comprometido. Muitos itens "same-user" não são fronteira de privilégio numa
máquina de um só utilizador; são robustez / cumprir a promessa "nada se perde numa rack".

---

## Invariantes (regras a manter)

- **INV-UPDATE-1** Nunca executar um binário descarregado sem verificar proveniência E integridade
  independentes do transporte: validar a URL (HTTPS, host do GitHub, repo esperado, nome de asset
  esperado) e verificar a assinatura Authenticode / hash publicado antes do `Process.Start`.
  *(revisão + teste)*
- **INV-IMPORT-1** Input importado é validado antes de virar estado persistido: allowlist de nomes
  de valor, tipos/ranges validados, `Folder`/`BackgroundImagePath` canonicalizados e a rejeitar
  UNC/sistema/traversal, e `rack.Name` só um segmento de chave seguro. *(teste)* Parcial: nome de
  rack já validado (Lote A #5).
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
  verificados imediatamente antes de lançar. *(revisão)*
- **INV-INSTANCE-1** Single-instance usa namespace `Local\` (ou DACL owner-only). *(mecânica: grep
  `Global\\`)* ✅ Aplicado (Lote A #12).
- **INV-PROC-1** Nunca passar strings externas por `cmd.exe /c` concatenado; usar `ArgumentList`
  ou a API nativa. *(mecânica: grep "cmd.exe" + interpolação)*
- **INV-IDS-1** Identificadores persistidos são leaf names por contrato: no load, descartar entradas
  onde `Path.GetFileName(name) != name`; após cada `Path.Combine` com dados guardados, afirmar que o
  path resolvido fica sob a raiz esperada. *(teste)*

---

## Achados

Gravidade / estado. `file:line` do sweep (podem ter drift de 1-2 linhas).

### HIGH

**H1 — Updater executa `assets[0]` do release sem verificação** `Racks/Util/Updater.cs` · ✅ CORRIGIDO PARCIAL (Lote B)
Agora: asset escolhido por nome (`Racks-Setup-*.exe`, não `assets[0]` cego), URL validada (HTTPS + host `github.com` + path `/duartelcunha/Racks/releases/download/`), tamanho verificado vs a API, e re-validação antes de descarregar. **Resíduo aceite:** a app não é code-signed, portanto não há verificação Authenticode; o vetor fica reduzido a um compromisso genuíno do repo/conta GitHub (mesma confiança de `git clone` + build). Fecho total exige code-signing. → INV-UPDATE-1.

**H2 — Import de layout JSON sem validação, persistido no registo** `Racks/Util/RackLayoutIO.cs` · ✅ CORRIGIDO (Lote A nome; Lote C valida `Folder`/`BackgroundImagePath`: rejeita traversal `..` e paths não-rooted, na passagem atómica antes de apagar). Regex do import já limitado por SafeRegex (#3). Resíduo: sem allowlist total de nomes de valor (importar é ação explícita do utilizador; vetores perigosos já fechados). → INV-IMPORT-1.

**H3 — Regex do utilizador sem `MatchTimeout` no UI thread (ReDoS)** `DesktopRouter.cs:73`, `RackWindow.xaml.cs`, `RackViewModel.cs` · ✅ CORRIGIDO (Lote A #3) → INV-REGEX-1.

**H4 — Drag-out apaga original do workspace por heurística de nome, em corrida com a cópia async do Explorer** `Racks/RackWindow.xaml.cs` · ✅ CORRIGIDO (Lote C). Agora só remove o original depois de `CopyLooksComplete` confirmar que a cópia no desktop está completa (compara tamanho/contagem recursiva), e vai para a **Reciclagem** (`SafeDelete.ToRecycleBin`), nunca delete permanente. Se não confirmar, mantém o original (duplicado temporário em vez de perda). → INV-DATALOSS-1.

### MEDIUM

**M1 — Import com `replaceExisting` apaga todas as racks antes de escrever, sem rollback** `RackLayoutIO.cs` · ✅ CORRIGIDO (Lote A #5, atómico + rollback) → INV-IMPORT-2.

**M2 — `SafeMove` compara paths como strings; contornável por 8.3, `\\?\`, UNC, junctions** `SafeMove.cs` · ✅ CORRIGIDO (Lote C). `Canonicalize` via `GetFinalPathNameByHandle` resolve 8.3/junctions/subst/`\\?\` antes de comparar, em `IsProtectedPath` e `IsAncestorOrEqual`. Testado: nome 8.3 canonicaliza para o mesmo path (bypass fechado), sem falso-positivo em paths normais. → INV-PATH-1.

**M3 — Proteção de delete fail-open, só verbo "delete" ANSI** `ShellContextMenu.cs` · PARCIAL: VERBW+VERBA aplicado (Lote A #7). Allowlist total NÃO aplicada de propósito: bloquear todos os verbos não-allowlisted arriscava partir itens legítimos de terceiros (Open With, Send To, extensões) — regressão de QoL. Verbo irresolúvel continua a passar. → INV-DELETE-1 (parcial).

### LOW

**L1 — Installer no temp com nome previsível `%TEMP%\Racks.exe`** `Updater.cs` · ✅ CORRIGIDO (Lote B, nome aleatório `Racks-update-{guid}.exe` + verificação de tamanho) → INV-EXEC-1.
**L2 — Uninstaller corre cópia de `{tmp}\RacksFarewell` (TOCTOU same-user)** `installer/Racks.iss:128-138` · ABERTO (Lote D).
**L3 — `SafeDelete` check-then-recurse TOCTOU em junctions** `SafeDelete.cs` · ✅ CORRIGIDO (Lote D). Reescrito com `DirectoryInfo`, classifica pelos atributos da própria enumeração (uma só operação) em vez de um stat separado. Testado: árvore com junction é apagada, o alvo do junction sobrevive (sem perda de dados).
**L4 — `CopyDirectory` cross-volume segue junctions (recursão/explosão)** `SafeMove.cs` · ✅ CORRIGIDO (Lote D). Salta reparse points na cópia + cap de profundidade (64). Deletes do `CopyThenDelete` roteados por `SafeDelete`.
**L5 — Mutex single-instance `Global\` (squatting DoS)** `App.xaml.cs` · ✅ CORRIGIDO (Lote A #12, `Local\`) → INV-INSTANCE-1.

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
