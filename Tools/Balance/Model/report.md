# Economy model — report

- Bot batch: `Bot\20261029-1336`.
- Gray weapon = first won Forest Easy run (with first clear bonus): **390**; prices by rarity: 390 / 1200 / 3100 / 7800 / 19000.
- Meta scale (upgrades, tree nodes) calibrated to 30 h: **0.150** → everything open in **30.2 h**, 75 runs.
- First weapon bought after run **41** (target — 1).
- Upgrade 1→10 costs **4.85×** the weapon's price (gray: 1890); a tree node 120–4500.
- Simulated player wins the last clear at power **10.51** (gray, no upgrades = 1).
- New clear paid less per minute than the best farm in **3** cases (target — 0: forward is better).
- Experience curve: `CardDeck._firstLevel` **11**, `_growth` **1.035** (14 cards in 10 min on Normal, every 30–60 s).

## Bot vs targets (Forest)

| Difficulty | Runs | Won | Minutes (bot) | Target | Waves cleared | Mass/min | Officer/min | Elite/min | Boss/min | Damage taken/min |
|---|---|---|---|---|---|---|---|---|---|---|
| Easy | 3 | 0 | 9.3 | 7 | 3.3 | 24.6 | 0.93 | 0.14 | 0.11 | 23 |
| Normal | 3 | 0 | 8.8 | 10 | 2.7 | 20.1 | 0.69 | 0.08 | 0.04 | 21 |
| Hard | 3 | 0 | 10.7 | 15 | 2.3 | 22.6 | 0.81 | 0.03 | 0.09 | 21 |
| Unreal | 3 | 0 | 12.0 | 20 | 1.3 | 12.6 | 0.36 | 0.00 | 0.03 | 18 |

## Income per run

| Level | Difficulty | Minutes | Run | First clear | Per min | Power needed |
|---|---|---|---|---|---|---|
| Forest | Easy | 7.0 | 194 | 194 | 27.7 | 1.0 |
| Forest | Normal | 10.0 | 481 | 481 | 48.1 | 1.67 |
| Forest | Hard | 15.0 | 1327 | 1327 | 88.5 | 2.6 |
| Forest | Unreal | 20.0 | 3208 | 3208 | 160.4 | 3.97 |
| Castle | Easy | 8.0 | 248 | 248 | 30.8 | 1.35 |
| Castle | Normal | 11.5 | 623 | 623 | 54.2 | 2.25 |
| Castle | Hard | 17.2 | 1736 | 1736 | 100.6 | 3.51 |
| Castle | Unreal | 23.0 | 4215 | 4215 | 183.2 | 5.35 |
| Snow | Easy | 9.3 | 317 | 317 | 34.3 | 1.82 |
| Snow | Normal | 13.2 | 809 | 809 | 61.2 | 3.04 |
| Snow | Hard | 19.8 | 2273 | 2273 | 114.6 | 4.74 |
| Snow | Unreal | 26.4 | 5541 | 5541 | 209.5 | 7.23 |

Tables: `economy.csv`, `prices.csv`, `upgrades.csv`, `throwables.csv`, `tree.csv`, `boosters.csv`, `experience.csv`, `progression.csv` (the simulated player, run by run).
