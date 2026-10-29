# Economy model — report

- Bot batch: `Bot\20261029-2237`.
- Gray weapon = first won Forest Easy run (with first clear bonus): **560**; prices by rarity: 560 / 1700 / 4500 / 11000 / 28000.
- Meta scale (upgrades, tree nodes) calibrated to 30 h: **0.188** → everything open in **29.6 h**, 83 runs.
- First weapon bought after run **66** (target — 1).
- Upgrade 1→10 costs **6.04×** the weapon's price (gray: 3380); a tree node 210–8100.
- Simulated player wins the last clear at power **10.51** (gray, no upgrades = 1).
- New clear paid less per minute than the best farm in **0** cases (target — 0: forward is better).
- Experience curve: `CardDeck._firstLevel` **11**, `_growth` **1.135** (14 cards in 10 min on Normal, every 30–60 s).

## Bot vs targets (Forest)

| Difficulty | Runs | Won | Minutes (bot) | Target | Waves cleared | Mass/min | Officer/min | Elite/min | Boss/min | Damage taken/min |
|---|---|---|---|---|---|---|---|---|---|---|
| Easy | 1 | 1 | 13.2 | 7 | 5.0 | 28.7 | 1.74 | 0.30 | 0.15 | 50 |
| Normal | 1 | 1 | 14.4 | 10 | 5.0 | 28.4 | 2.29 | 0.28 | 0.14 | 69 |
| Hard | 1 | 0 | 11.6 | 15 | 4.0 | 29.1 | 1.89 | 0.17 | 0.09 | 132 |
| Unreal | 1 | 0 | 7.7 | 20 | 2.0 | 18.8 | 0.52 | 0.00 | 0.00 | 114 |

## Income per run

| Level | Difficulty | Minutes | Run | First clear | Per min | Power needed |
|---|---|---|---|---|---|---|
| Forest | Easy | 7.0 | 282 | 282 | 40.3 | 1.0 |
| Forest | Normal | 10.0 | 735 | 735 | 73.5 | 1.67 |
| Forest | Hard | 15.0 | 2288 | 2288 | 152.5 | 2.82 |
| Forest | Unreal | 20.0 | 5866 | 5866 | 293.3 | 4.39 |
| Castle | Easy | 8.0 | 361 | 361 | 44.8 | 1.35 |
| Castle | Normal | 11.5 | 954 | 954 | 83.0 | 2.25 |
| Castle | Hard | 17.2 | 2999 | 2999 | 173.8 | 3.8 |
| Castle | Unreal | 23.0 | 7718 | 7718 | 335.6 | 5.93 |
| Snow | Easy | 9.3 | 462 | 462 | 49.9 | 1.82 |
| Snow | Normal | 13.2 | 1241 | 1241 | 93.9 | 3.04 |
| Snow | Hard | 19.8 | 3934 | 3934 | 198.3 | 5.13 |
| Snow | Unreal | 26.4 | 10161 | 10161 | 384.2 | 8.0 |

Tables: `economy.csv`, `prices.csv`, `upgrades.csv`, `throwables.csv`, `tree.csv`, `boosters.csv`, `experience.csv`, `progression.csv` (the simulated player, run by run).
