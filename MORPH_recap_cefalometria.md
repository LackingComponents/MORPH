# MORPH — Recap motore cefalometria multi-analisi

**Progetto:** OrthoPlanner (MORPH), pianificazione chirurgica ortognatica (BSSO, LeFort I, genioplastica)
**Repo:** github.com/LackingComponents/MORPH
**Lavoro attuale su:** branch `experimental_Lore`, cartella `C:\Users\fdrln\Desktop\CMF PLANNER\MORPHE_EXPERIMENTAL`
(nota: diverso da `main`, che resta il branch stabile del progetto)

---

## Obiettivo di questo filone

Aggiungere un motore che calcola automaticamente più tipi di analisi cefalometrica (Steiner, Tweed, Ricketts, in futuro McNamara/Arnett) a partire dai 42 landmark Badiali già digitalizzati, mostrando i risultati in schede separate per tipo di analisi — sul modello di IPS CaseDesigner / webceph, non un sistema di auto-detection ML.

---

## Decisioni architetturali prese (non rimetterle in discussione senza motivo)

- **Base di calcolo: 2D, in pixel-space della DRR** (calibrata in mm), riusando `CephToolEngine` esistente — non un motore 3D nativo sulle coordinate X,Y,Z.
- **Punti bilaterali (Po, Or, Go, ecc.): sempre punto medio L/R.** Se solo un lato è digitalizzato, la misura risulta `MissingLandmarks` — mai fallback silenzioso su un lato solo.
- **Angolo interincisale e altri angoli ottusi:** calcolati via intersezione degli assi, non col metodo ingenuo delle rette non direzionate (evita l'errore classico "angolo acuto per costruzione").
- **Fase 1 = solo motore di calcolo, zero modifiche UI.** Le schede/tabella risultati sono Fase 2, non ancora iniziata.

## Scope Fase 1 (cosa è dentro, cosa è fuori)

**Dentro:**
- Steiner: SNA, SNB, ANB, SN-GoGn, U1-NA (angolo+mm), L1-NB (angolo+mm), interincisale
- Tweed: FMA, FMIA, IMPA
- Ricketts ridotto: Facial Depth, Mandibular Plane, Convexity

**Fuori (rimandato):**
- McNamara — norme età/sesso-dipendenti, servono di più le fonti esatte prima di implementarlo
- Ricketts Facial Axis / Facial Taper — richiedono il punto Pt (landmark anatomico singolo, ok) e il punto Xi (**non è un landmark, è un punto costruito**: rettangolo tangente a 4 punti sul bordo del ramo mandibolare, centro = intersezione diagonali — lavoro paragonabile a una feature a sé)
- Arnett (analisi tessuti molli) — servono 9-12 nuovi landmark soft-tissue che oggi non esistono nel set (categoria `LandmarkCategory` ha solo `Skeletal`/`Dental`)

---

## Stato implementazione Fase 1

✅ Prompt Phase 0 (diagnostico, read-only) eseguito — ha mappato dove vivono i 42 landmark, lo storage, `CephToolEngine`, l'assenza di norme/tabelle
✅ Prompt Phase 1 (implementazione motore) eseguito da Claude Code
✅ Build pulita, app builda e runna correttamente
✅ Nuova cartella creata: `src/OrthoPlanner.Core/Imaging/Cephalometry/` — nessun file UI/ViewModel toccato
✅ Verifica sintetica ricevuta parzialmente: Case B (distanza U1-NA con midpoint), Case C (angolo interincisale ottuso 135°), Case D (nessun fallback silenzioso su un lato solo), Case E (propagazione MissingLandmarks su ANB derivato) — **tutti matematicamente corretti, controllati a mano**

⚠️ **INCOMPLETO — da recuperare prima di procedere:**
1. **Case A mancante** — il report faceva riferimento a "Take set A" ma il testo del Case A non è stato incollato in chat. Serve per verificare il caso base SNA/SNB/ANB.
2. **Conferma esplicita se esiste un progetto di test** (xUnit/NUnit/MSTest) nella solution — il prompt lo richiedeva esplicitamente, non è stata data una risposta diretta.

---

## Prossimo passo immediato

Tornare alla chat/sessione di Claude Code dove è stato eseguito il prompt Fase 1, scorrere in alto nell'output e recuperare:
- il testo completo del **Case A**
- la frase che conferma/nega l'esistenza di un progetto di test

Incollare questo output nella nuova chat per chiudere la verifica prima di passare alla Fase 2.

---

## Roadmap

1. **Chiudere verifica Fase 1** (Case A + conferma test project) ← prossimo passo
2. **Fase 2 — wiring UI**: schede separate per tipo di analisi, tabella risultati con binding vero (oggi la UI cefalometria è costruita in modo imperativo, senza binding — da rivedere per questa feature)
3. (Più avanti, non ora) **Pt come landmark singolo** se serve per altri scopi
4. (Più avanti, non ora) **Costruzione punto Xi** (4 nuovi landmark R1-R4 + algoritmo geometrico) per sbloccare Facial Axis/Facial Taper
5. (Più avanti, non ora) **Landmark soft-tissue** per Arnett

---

## Altri fronti aperti nel progetto (per contesto, non attivi in questo filone)

- **Bug splint piatto**: `SplintEngine.cs`, sospetto mismatch di coordinate, trace log in `%TEMP%\splint_trace.txt` già in place, diagnosi non ancora completata. Branch `feature/splint-clinical` parcheggiato, non contiene il fix.
- **Import lastra 2D laterale standalone + calibrazione**: discusso concettualmente (calibrazione pixel→mm via punti a distanza nota o lettura PixelSpacing DICOM), non ancora iniziato architetturalmente.
- **OCR libro scannerizzato** (Baidu Unlimited-OCR su RTX 4060/WSL2): setup a metà, `wsl --status` e `nvidia-smi` non ancora eseguiti.

---

## Promemoria workflow (per la nuova chat)

- Ogni prompt per coding agent: **in inglese**, verifica branch obbligatoria all'inizio, sezione file protetti, task singolo, no batch di modifiche.
- Diagnosi prima di implementare, sempre. Un passo alla volta: mai review + edit nella stessa sessione agente.
- Build di verifica: `dotnet build OrthoPlanner.sln --configuration Debug --no-incremental`
- File protetti (mai toccare senza approvazione esplicita): `MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml.cs`, `Polyplane.cs`, `BoolConverters`, `AppTempStorage`
