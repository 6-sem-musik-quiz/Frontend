# Lydeffekter (SCRUM-161)

CC0-lyde fra Kenney (kenney.nl), se `LICENSE-kenney.txt`. Klippet til, gjort mono, low-passet ved
5,5 kHz og normaliseret til samme peak (-8 dBFS). Afspilles af `wwwroot/js/sfx.js`, som også styrer
den indbyrdes lydstyrke pr. lyd.

| Fil | Kilde (pakke/fil) | Bruges til |
|---|---|---|
| lobbyJoin | Interface Sounds / open_002 | Spiller joiner lobbyen |
| lobbyLeave | Interface Sounds / close_002 | Spiller forlader lobbyen |
| ready | Casino Audio / chip-lay-2 | Klar |
| unready | Casino Audio / chips-handle-1 | Ikke klar (chippen tages op igen) |
| lobbyFull | Interface Sounds / glass_002 | Sidste plads taget |
| allReady | Casino Audio / chips-stack-3 | Alle er klar |
| gameStart | Interface Sounds / confirmation_002 | Spillet starter |
| click | UI Audio / click3 | Klik på knapper |
| hover | UI Audio / rollover2 | Musen over knapper og links |
| cardPickup | Interface Sounds / select_001 | Kortet tages op (blødt klik) |
| cardMove | UI Audio / rollover3 | Pladsen under kortet skifter (svagt tik) |
| cardPlace | Interface Sounds / drop_001 | Kortet slippes (i hånden eller tilbage) |
| lockIn | Casino Audio / chip-lay-3 | Bekræft (svaret låses) |
| tick | Interface Sounds / tick_002 | De sidste 5 sekunder |
| timeUp | Interface Sounds / bong_001 | Tiden er gået |
| correct | Casino Audio / chips-stack-5 | Rigtigt svar |
| wrong | Interface Sounds / back_002 | Forkert eller intet svar |
| winner | Interface Sounds / confirmation_004 | Spillet slut, 1. plads |
| gameOver | Casino Audio / chips-stack-6 | Spillet slut, ikke 1. plads |

## Slutskærmen (SCRUM-155)

Ingen nye filer: lydene genbruger filerne ovenfor med deres egen lydstyrke (`file` i `sfx.js`).
De spilles kun, når afsløringen kører i sit eget tempo; "Spring til resultatet" springer dem over.
Bonuspriserne (Lynet: hurtigst, Stimen: flest rigtige i træk) er de tydeligste.

| Lyd | Fil | Bruges til |
|---|---|---|
| bonusIntro | gameStart | "Bonus!" på scenen |
| awardReveal | gameStart | En bonuspris (Lynet, Stimen) kommer frem |
| awardValue | tick | En deltagers tid eller stime vises |
| awardStamp | correct | +1 bonuspoint lander på prisens vinder ("ka-ching") |
| standingsIntro | lobbyJoin | "Slutstilling" |
| placementReveal | cardPlace | En placering vendes (kort lægges) |
| winnerSuspense | lobbyFull | "Og vinderen er …" |
| (vinderens navn) | winner | Fanfaren, for alle, når vinderen afsløres |
