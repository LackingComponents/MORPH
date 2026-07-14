# MORPH — Recap motore cefalometria multi-analisi (v2, dopo fix IMPA/Facial Depth)

**Progetto:** OrthoPlanner (MORPH), pianificazione chirurgica ortognatica (BSSO, LeFort I, genioplastica)
**Repo:** github.com/LackingComponents/MORPH
**Lavoro attuale su:** branch `experimental_Lore`, cartella `C:\Users\fdrln\Desktop\CMF PLANNER\MORPHE_EXPERIMENTAL`
(nota: diverso da `main`, che resta il branch stabile del progetto)

---

## Stato attuale — commit su experimental_Lore (in ordine cronologico)

1. **`a956a38`** — Fix del file `OrthoPlanner.sln`: mancava la sezione `Global` / `GlobalSection(SolutionConfigurationPlatforms)`. Causava una build a livello di soluzione che sembrava riuscita ("0 errori") ma in realtà non compilava nulla ("nessun progetto da ripristinare", istantanea). Risolto aggiungendo la sezione mancante, verificato con build reale (~18-22s, entrambe le DLL emesse).
2. **`50e946c`** — Motore cefalometrico multi-analisi Fase 1 (Steiner, Tweed, Ricketts ridotto), 5 file, 410 righe. Era rimasto **non committato per giorni** (file non tracciati) prima di questa sessione.
3. **`8c1b257`** — Fix di IMPA e Facial Depth: sostituito `CephToolEngine.AngleLines` (che "ripiega" sempre all'angolo acuto 0-90°) con il metodo intersezione-assi + `Angle3Pts` (0-180°), lo stesso già usato per l'angolo interincisale in Fase 1.

## Verifiche fatte (tutte controllate a mano, matematica confermata corretta)

- Case B, C, D, E (Fase 1): midpoint bilaterale, angolo interincisale ottuso, nessun fallback silenzioso su un lato solo, propagazione MissingLandmarks
- Case A (Fase 1): SNA=90°, SNB=45°, ANB=45° (caso sintetico estremo, non clinico, ma matematica corretta)
- Case F1: IMPA con incisivo proclinato → 108.43° (prima del fix: 71.57°, sbagliato)
- Case F2: Facial Depth con mento prognatico (Classe III) → 100.62° (prima del fix: 79.38°, sbagliato)
- Confermato: nessun progetto di test (xUnit/NUnit/MSTest) esiste nella solution — verifica sempre tramite casi sintetici controllati a mano

## Aperto — non ancora implementato

**Misure lineari con segno** (U1-NA, L1-NB, Ricketts Convexity): oggi restituiscono solo distanza assoluta, ma clinicamente sono firmate (anteriore/posteriore). Diagnosi già fatta (non l'implementazione):
- **Convenzione clinica confermata da Lore: si usa sempre il profilo destro.** Questo fissa l'orientamento una volta per tutte, semplifica la scelta del segno.
- Non esiste nel codice un flag "direzione anteriore" già pronto — va costruito, o dedotto geometricamente dai landmark.
- `CephToolEngine.PerpendicularToLine` ha 5 punti di utilizzo in tutta la solution: 3 nel motore cefalometrico (vogliono il segno), 2 nell'overlay UI interattivo (vogliono il valore assoluto — si romperebbero con un cambio di segno). **Raccomandazione già validata: non toccare la firma condivisa, aggiungere un metodo/helper nuovo apposta per il segno.**

## Prossimi passi (roadmap)

1. **Implementare le distanze con segno** (U1-NA, L1-NB, Ricketts Convexity) — prossimo passo naturale, usando la convenzione "profilo destro" confermata
2. **Fase 2 — wiring UI**: schede separate per tipo di analisi, tabella risultati con binding vero (oggi costruita in modo imperativo)
3. *(Più avanti, non ora)* Punto Pt come landmark singolo
4. *(Più avanti, non ora)* Costruzione punto Xi (4 nuovi landmark R1-R4 + algoritmo geometrico) per sbloccare Ricketts Facial Axis/Facial Taper
5. *(Più avanti, non ora)* Landmark soft-tissue per Arnett

---

## Apprendimenti tecnici nuovi da questa sessione (utili anche fuori dalla cefalometria)

- **Bug di build risolto**: se su un'altra macchina/clone la build a livello di soluzione (`dotnet build OrthoPlanner.sln`) sembra "riuscire" in una frazione di secondo senza compilare nulla, controllare per primo se manca la sezione `Global`/`GlobalSection(SolutionConfigurationPlatforms)` nel file `.sln` — è successo una volta, potrebbe ripetersi su altri branch/copie.
- Esiste un file di configurazione globale (`git-workflow.md`) che disabilita il trailer di attribuzione nei commit — spiega perché i commit di Claude Code non hanno la riga "Co-authored-by" o simili.
- **Lezione di processo**: la cartella `Cephalometry/` è rimasta non tracciata da Git per giorni prima di essere committata. D'ora in poi, committare subito dopo ogni build verificata, non aspettare.

---

## Altri fronti aperti nel progetto (per contesto, non attivi in questo filone)

- **Bug splint piatto**: `SplintEngine.cs`, sospetto mismatch di coordinate, trace log in `%TEMP%\splint_trace.txt` già in place, diagnosi non ancora completata. Branch `feature/splint-clinical` parcheggiato, non contiene il fix.
- **Import lastra 2D laterale standalone + calibrazione**: discusso concettualmente, non ancora iniziato architetturalmente.
- **OCR libro scannerizzato** (Baidu Unlimited-OCR su RTX 4060/WSL2): setup a metà, `wsl --status` e `nvidia-smi` non ancora eseguiti.

---

## Promemoria workflow (invariato, vale anche su Cursor)

- Ogni prompt per coding agent: **in inglese**, verifica branch obbligatoria all'inizio, sezione file protetti, task singolo, no batch di modifiche.
- Diagnosi prima di implementare, sempre. Un passo alla volta: mai review + edit nella stessa sessione agente.
- **Un task = un commit.** Non lasciare lavoro non tracciato per giorni (vedi lezione sopra).
- Build di verifica: `dotnet build OrthoPlanner.sln --configuration Debug --no-incremental`
- File protetti (mai toccare senza approvazione esplicita): `MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml.cs`, `Polyplane.cs`, `BoolConverters`, `AppTempStorage`
- Algoritmi bloccati (mai proporne la sostituzione): pipeline DRR ray-sum, sistema 42-landmark Badiali, sequenza Clean & Merge Cast
