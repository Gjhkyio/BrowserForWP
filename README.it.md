# BrowserForWP

**Un browser con trasporto moderno per Windows Phone 8.1 — interamente sul
dispositivo, senza alcun backend.**

[English](README.md) · [Italiano](README.it.md)

---

## Da leggere subito: cosa è realmente possibile su Windows Phone 8.1

Questo progetto fa una promessa insolitamente onesta, quindi ecco la verità
tecnica prima di qualsiasi elenco di funzionalità.

Windows Phone 8.1 è una **piattaforma chiusa, pubblicata nel 2014 e
abbandonata a luglio 2017**. Il suo stack di navigazione è **Trident (Internet
Explorer 11)** e il sistema operativo **non offre alle app di terze parti alcun
modo di sostituire il motore di rendering**. È un limite strutturale del
sistema, non un limite delle ambizioni di questo progetto.

| Obiettivo | Realtà su Windows Phone 8.1 | Cosa fa BrowserForWP |
| --- | --- | --- |
| Includere il motore **Chromium** | Non esiste alcuna build di Chromium/Blink per WinRT-ARM 8.1. I container delle app non possono ospitare un renderer multi-processo in sandbox. | Fornisce un `IBrowserEngine` sostituibile. Su WP8.1 distribuisce `TridentEngine`; `WebView2Engine` (Chromium) e `GeckoViewEngine` (Firefox) si innestano su qualunque piattaforma li possieda. |
| Includere il motore **Firefox / Gecko** | Mozilla ha cancellato Firefox per Windows Phone nel 2015. Nessun binario è mai stato distribuito. | Stessa astrazione sostituibile di cui sopra. |
| **TLS 1.3** | Schannel su WP8.1 si ferma a **TLS 1.2** e il sistema non espone alcuna API per alzare il limite. | **Implementato dalle RFC, in codice gestito, sul dispositivo**: un client TLS 1.3 completo (`BrowserForWP.Net`) che gira su un `StreamSocket` grezzo, così il livello di rete dell'app parla TLS 1.3 già oggi. |
| **HTTPS moderno** | La `WebView` di sistema negozia ciò che Schannel supporta. | `Tls13Client` + resolver DNS-over-HTTPS + pinning dei certificati per il livello di trasporto dell'app. |
| **Pagine web moderne** | IE11 non riesce a interpretare né a eseguire il JavaScript moderno. | Una pipeline di polyfill/transpilazione sul dispositivo, iniettata in ogni pagina (`BrowserForWP.Core`), più una diagnostica di compatibilità che spiega *perché* un sito continua a fallire. |
| **Nessun backend** | — | Ogni componente — crittografia, TLS, DNS, polyfill, cronologia, localizzazione — gira interamente sul telefono. Nessun server, nessun servizio proxy, nessuna telemetria. |

> **Sull'idea del proxy locale sul dispositivo:** i Windows AppContainer
> bloccano per impostazione predefinita il traffico verso `127.0.0.1`, quindi un
> proxy locale non può alimentare la `WebView` di sistema. Questa architettura è
> volutamente **non** utilizzata. Vedi
> [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) per l'analisi completa dei
> vincoli.

**In sintesi:** ottieni un livello di *trasporto* realmente moderno (TLS 1.3,
DoH, pinning) e un livello di *contenuto* realmente moderno (polyfill), su un
livello di *rendering* invariato — perché su questo sistema il rendering non è
modificabile. L'astrazione del motore significa che il giorno in cui punterai
questo codice a un dispositivo con un motore moderno vero, i livelli di
trasporto e contenuto verranno con te.

---

## Funzionalità

- **Interfaccia browser completa** — barra degli indirizzi, indietro / avanti /
  ricarica / stop, schede, indicatore di avanzamento, stato di sicurezza per
  sito.
- **Barra degli indirizzi intelligente** — normalizza l'input, distingue URL da
  query di ricerca, ripristina gli schemi mancanti e rifiuta gli schemi
  pericolosi.
- **Trasporto TLS 1.3 (RFC 8446)** — implementazione gestita su
  `Windows.Networking.Sockets.StreamSocket`.
- **DNS over HTTPS (RFC 8484)** — risolve i nomi host fuori dal canale
  tradizionale, così un resolver locale obsoleto o dirottato non può rompere o
  redirigere la navigazione.
- **Pinning dei certificati** — pin per sito gestiti dall'utente, con override
  esplicito e reversibile.
- **Iniezione di polyfill** — un livello di compatibilità curato, sul
  dispositivo, viene iniettato in ogni documento prima dell'esecuzione degli
  script.
- **Interfaccia bilingue** — inglese e italiano, scelti automaticamente dalla
  lingua di visualizzazione del telefono, con override per singola app.
- **Diagnostica** — una sonda integrata che segnala esattamente quale
  funzionalità moderna ha causato il fallimento di una pagina, così il limite è
  visibile invece che misterioso.

## Struttura del repository

```
BrowserForWP/
├── BrowserForWP.sln            Soluzione Visual Studio 2013+
├── BrowserForWP/              App Windows Phone 8.1 (VB.NET / WinRT / XAML)
│   ├── MainPage.xaml(.vb)     Interfaccia del browser
│   ├── Assets/                Logo, tile, splash (generati)
│   └── Strings/               Risorse UI en-US / it-IT
├── BrowserForWP.Core/         Astrazione del motore, schede, cronologia
├── BrowserForWP.Net/          TLS 1.3, DoH, client HTTP
├── BrowserForWP.Crypto/       HKDF, X25519, ChaCha20-Poly1305, AES-GCM
├── BrowserForWP.Localization/ Risoluzione della lingua + lookup delle stringhe
├── BrowserForWP.Polyfill/     Bundle JS di compatibilità sul dispositivo
├── docs/
│   ├── ARCHITECTURE.md        Progettazione + analisi dei vincoli di piattaforma
│   ├── MAINTAINING.md         Come compilare, eseguire ed estendere il progetto
│   └── superpowers/plans/     Piani di implementazione (uno per funzionalità)
├── tests/                     Test unitari (eseguiti in Visual Studio)
├── tools/
│   ├── make_logo.py           Rigenera tutte le immagini partendo dall'SVG
│   └── gen-vectors.mjs        Genera i vettori noti delle RFC per i test
└── .agents/skills/browserforwp/SKILL.md
                               Skill del progetto: piano → commit → push → estensione
```

## Compilazione

Requisiti: **Visual Studio 2013 Update 4 o successivo** con *Windows Phone 8.1
SDK*, su Windows. La soluzione ha come target `TargetPlatformVersion 8.1` e
`WindowsPhoneApp`.

```
1. Apri BrowserForWP.sln
2. Seleziona un target telefono: Debug | ARM  (dispositivo) oppure Debug | x86 (emulatore)
3. Distribuisci su un telefono sbloccato per sviluppatori o sull'emulatore WP8.1
```

> Il motore di rendering, la crittografia e il codice TLS non possono essere
> eseguiti su macOS o Linux: l'SDK WP8.1 è solo per Windows. La crittografia
> puramente gestita in `BrowserForWP.Crypto` è rispecchiata da `tests/`, che
> viene eseguito in Visual Studio.

Rigenera immagini e vettori di test in qualsiasi momento:

```bash
python3 tools/make_logo.py          # riscrive BrowserForWP/Assets/*.png
node tools/gen-vectors.mjs          # riscrive tools/out/*.json
```

## Localizzazione

L'app include **en-US** (predefinita) e **it-IT**. La lingua di visualizzazione
viene scelta in quest'ordine:

1. Un override per singola app, se l'utente ne ha impostato uno.
2. `Windows.Globalization.ApplicationLanguages.Languages` — l'elenco ordinato
   delle lingue del telefono.
3. Fallback: `en-US`.

Aggiungere una lingua significa aggiungere una cartella di risorse e una voce
nella tabella delle lingue — nessuna modifica al codice. Vedi
[`docs/MAINTAINING.md`](docs/MAINTAINING.md#aggiungere-una-lingua).

## Contribuire

Leggi [`docs/MAINTAINING.md`](docs/MAINTAINING.md) per il flusso di
compilazione/test e [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) prima di
modificare i livelli del motore o del trasporto. La skill del progetto in
[`.agents/skills/browserforwp/SKILL.md`](.agents/skills/browserforwp/SKILL.md)
descrive il ciclo obbligatorio piano → test → commit → push.

## Licenza

[MIT](LICENSE) © 2026 vincenzosco

---

## Dichiarazione: questo progetto è fatto al 100% da IA

Ogni parte di BrowserForWP — l'architettura, lo stack TLS 1.3, la crittografia,
l'interfaccia, gli strumenti, la documentazione e questa nota — è stata scritta
da un agente di programmazione IA, con una persona che ha diretto il lavoro e
rivisto il risultato passo per passo.

È una affermazione concreta su quanto fidarsi, quindi ecco la posizione onesta
invece che un vanto:

- **Qui non è stato compilato nulla.** Sulla macchina che ha scritto il codice
  non erano disponibili né l'SDK di Windows Phone 8.1 né Visual Studio 2013,
  quindi il codice VB.NET non è mai passato da un compilatore vero.
  `tools/check-vb.mjs` esegue i controlli meccanici riproducibili fuori da
  Windows e ha trovato difetti reali — ma non è un compilatore, e un esito
  positivo non significa che compili.
- **La crittografia e il protocollo TLS 1.3 sono verificati, ma non su un
  telefono.** Passano `tools/gen-vectors.mjs` (52 asserzioni contro RFC
  5869/7748/8439/8448 e NIST AES-GCM), `tools/proto/w25519.mjs` (18 controlli) e
  `tools/proto/tls13.mjs` (31 controlli, con handshake reali verso Google,
  Cloudflare ed example.com).
- **Non è mai stato eseguito su un telefono.** Layout XAML, comportamento del
  WebView e prestazioni su hardware del 2014 non sono verificati.
- **Un tentativo di compilazione reale è stato fatto e riportato con onestà.**
  La VM Windows disponibile è ARM64, dove Microsoft non supporta Visual Studio
  precedente alla 17.4 e dove l'SDK WP8.1 non ha target di compilazione. È
  documentato in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) invece di essere
  omesso.
- **Le affermazioni sono state verificate e quelle false eliminate.** Chromium e
  Firefox non possono girare su questo sistema operativo e TLS 1.3 non è
  ottenibile da esso; entrambi i fatti sono dichiarati apertamente. Il lavoro
  che *era* possibile — uno stack TLS 1.3 scritto da zero — è stato fatto e
  verificato.

Consideralo un punto di partenza ben documentato che richiede ancora una
compilazione reale e una prova su dispositivo, non un prodotto finito.
