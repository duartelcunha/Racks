# Security invariants and findings (Racks)

Registo de segurança do Racks. Cada invariante é uma regra que previne uma classe inteira.
Origem: sweep multi-agente read-only (5 lentes + síntese) em 2026-07-11.

Estado dos fixes: **Lote A aplicado** (commit desta ronda). Lotes B/C/D em aberto, à espera de decisão.

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

**H1 — Updater executa `assets[0]` do release sem verificação** `Racks/Util/Updater.cs:36,131,191` · ABERTO (Lote B)
Descarrega e corre o primeiro asset do último release. Sem Authenticode, sem hash, sem validar host/repo/nome. TLS OK (vetor = release comprometido / takeover de conta, não MITM). Ponto mais crítico. → INV-UPDATE-1.

**H2 — Import de layout JSON sem validação, persistido no registo** `Racks/Util/RackLayoutIO.cs` · PARCIAL (nome validado em Lote A; falta canonicalizar Folder/BackgroundImagePath, allowlist de valores) → INV-IMPORT-1.

**H3 — Regex do utilizador sem `MatchTimeout` no UI thread (ReDoS)** `DesktopRouter.cs:73`, `RackWindow.xaml.cs`, `RackViewModel.cs` · ✅ CORRIGIDO (Lote A #3) → INV-REGEX-1.

**H4 — Drag-out apaga original do workspace por heurística de nome, em corrida com a cópia async do Explorer** `Racks/RackWindow.xaml.cs:1635-1648` · ABERTO (Lote C) · SUSPECTED. Delete permanente (sem Reciclagem) pode destruir a fonte a meio da cópia. → INV-DATALOSS-1.

### MEDIUM

**M1 — Import com `replaceExisting` apaga todas as racks antes de escrever, sem rollback** `RackLayoutIO.cs` · ✅ CORRIGIDO (Lote A #5, atómico + rollback) → INV-IMPORT-2.

**M2 — `SafeMove` compara paths como strings; contornável por 8.3, `\\?\`, UNC, junctions** `SafeMove.cs:164-207` · ABERTO (Lote C) → INV-PATH-1.

**M3 — Proteção de delete fail-open, só verbo "delete" ANSI** `ShellContextMenu.cs` · PARCIAL: VERBW+VERBA aplicado (Lote A #7). Allowlist total NÃO aplicada de propósito: bloquear todos os verbos não-allowlisted arriscava partir itens legítimos de terceiros (Open With, Send To, extensões) — regressão de QoL. Verbo irresolúvel continua a passar. → INV-DELETE-1 (parcial).

### LOW

**L1 — Installer no temp com nome previsível `%TEMP%\Racks.exe`** `Updater.cs:137` · ABERTO (Lote D) → INV-EXEC-1.
**L2 — Uninstaller corre cópia de `{tmp}\RacksFarewell` (TOCTOU same-user)** `installer/Racks.iss:128-138` · ABERTO (Lote D).
**L3 — `SafeDelete` check-then-recurse TOCTOU em junctions** `SafeDelete.cs:25-35` · ABERTO (Lote D) → INV-DELETE-2.
**L4 — `CopyDirectory` cross-volume segue junctions (recursão/explosão)** `SafeMove.cs:151-162` · ABERTO (Lote D).
**L5 — Mutex single-instance `Global\` (squatting DoS)** `App.xaml.cs` · ✅ CORRIGIDO (Lote A #12, `Local\`) → INV-INSTANCE-1.

### INFO

**I1 — `mklink` via `cmd.exe` com interpolação (latente, não explorável hoje)** `JunctionHelper.cs:165` → INV-PROC-1.
**I2 — `AssignedFiles` do HKCU em `Path.Combine` sem rejeitar `..` (same-user)** `RackWindow.xaml.cs:5119,1595` → INV-IDS-1.
**I3 — Comentários dizem que `Directory.Delete(recursive)` segue junctions; no .NET 10 não segue** `JunctionHelper.cs:17-19` · risco de manutenção → INV-DELETE-2.

---

## Segredos / exposição no GitHub

Verificado em 2026-07-11: **zero segredos** em ficheiros tracked ou no histórico git. Endpoints só
GitHub API/repo e ko-fi. `.gitignore` endurecido (`.env`, `secrets.json`, `*.pfx/.snk/.pem/.key`,
`*.reg`, etc.). Removido `test/last-test-result.json` (artefacto gerado, sem PII).
