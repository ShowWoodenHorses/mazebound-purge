"""Economy model of Mazebound: Purge (stage 10b).

Inputs: params.json (targets and knobs), Data/*.csv (Purge -> Balance -> Export Tables), the bot batch
(Bot/<batch>/runs.csv, waves.csv). Outputs: Model/*.csv and Model/report.md.

Run: python Tools/Balance/model.py [--batch Bot/<folder>]
"""
import argparse
import csv
import json
import math
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
RANKS = ["Mass", "Officer", "Elite", "Boss"]
RARITIES = ["Common", "Uncommon", "Rare", "Epic", "Legendary"]


# ---------- input ----------

def read_csv(path):
    with open(path, newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def write_csv(path, header, rows):
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(header)
        for row in rows:
            w.writerow([fmt(v) for v in row])


def fmt(v):
    if isinstance(v, float):
        return f"{v:.3f}".rstrip("0").rstrip(".") if abs(v) < 1000 else f"{v:.0f}"
    return v


def num(s, default=0.0):
    try:
        return float(s)
    except (TypeError, ValueError):
        return default


def asset_name(path):
    return os.path.splitext(os.path.basename(path))[0]


def nice(value, step=10):
    """Rounds to two significant digits, but not finer than `step`."""
    if value <= 0:
        return 0
    digits = 10 ** max(0, int(math.floor(math.log10(value))) - 1)
    return max(step, int(round(value / max(digits, step)) * max(digits, step)))


def latest_batch(root):
    folders = sorted(d for d in os.listdir(root) if os.path.isfile(os.path.join(root, d, "runs.csv")))
    return os.path.join(root, folders[-1]) if folders else None


# ---------- bot statistics ----------

class BotStats:
    """Means of the bot batch per (level, difficulty)."""

    def __init__(self, batch):
        self.batch = batch
        self.groups = defaultdict(list)
        self.waves = defaultdict(list)
        if batch is None:
            return
        for row in read_csv(os.path.join(batch, "runs.csv")):
            if row["outcome"] in ("failed", "error") or num(row["minutes"]) < 1:
                continue
            self.groups[(row["level"], row["difficulty"])].append(row)
        waves = os.path.join(batch, "waves.csv")
        if os.path.isfile(waves):
            for row in read_csv(waves):
                self.waves[(row["level"], row["difficulty"], row["run"])].append(row)

    def has(self, level, difficulty):
        return bool(self.groups.get((level, difficulty)))

    def summary(self, level, difficulty):
        rows = self.groups.get((level, difficulty), [])
        if not rows:
            return None
        minutes = sum(num(r["minutes"]) for r in rows)
        s = {
            "runs": len(rows),
            "won": sum(r["outcome"] == "won" for r in rows),
            "minutes": minutes / len(rows),
            "waves": sum(num(r["waves_cleared"]) for r in rows) / len(rows),
            "other_pm": sum(num(r["money"]) - num(r["kill_money"]) - num(r["wave_money"]) for r in rows) / minutes,
            "xp_pm": sum(num(r["experience"]) for r in rows) / minutes,
            "damage_pm": sum(num(r["damage_taken"]) for r in rows) / minutes,
        }
        for rank in RANKS:
            s["kpm_" + rank] = sum(num(r["kills_" + rank.lower()]) for r in rows) / minutes
        return s

    def xp_curve(self, level, difficulty):
        """Mean cumulative experience at time t (seconds) over the runs: list of (t, xp) per run."""
        curves = []
        for (lv, df, _), rows in self.waves.items():
            if lv != level or df != difficulty:
                continue
            points, total = [(0.0, 0.0)], 0.0
            for r in sorted(rows, key=lambda r: num(r["wave"])):
                total += num(r["experience"])
                points.append((num(r["start_s"]) + num(r["duration_s"]), total))
            curves.append(points)
        return curves


def interpolate(points, t):
    for (t0, x0), (t1, x1) in zip(points, points[1:]):
        if t <= t1:
            return x0 + (x1 - x0) * (t - t0) / max(t1 - t0, 1e-6)
    (ta, xa), (tb, xb) = points[-2], points[-1]
    rate = (xb - xa) / max(tb - ta, 1e-6)  # the last wave's pace continues
    return xb + rate * (t - tb)


# ---------- model ----------

class Model:
    def __init__(self, params, data_dir, bot):
        self.p = params
        self.bot = bot
        self.notes = []
        self.difficulties = params["difficulties"]
        self.diff = {}
        for row in read_csv(os.path.join(data_dir, "difficulties.csv")):
            self.diff[asset_name(row["asset"])] = {k: num(row["_" + k], 1.0) for k in ("health", "count", "money", "seniorShare")}
        self.wave_bonus = self.read_wave_bonus(data_dir)
        self.levels = [lv["name"] for lv in params["levels"]]
        self.price_one = None
        self.scale = 1.0
        self._pooled = {}

    def read_wave_bonus(self, data_dir):
        bonus = {}
        for row in read_csv(os.path.join(data_dir, "waves.csv")):
            total, n = 0.0, 0
            while f"_waves[{n}]._budget" in row and row[f"_waves[{n}]._budget"] != "":
                given = num(row.get(f"_waves[{n}]._moneyBonus"))
                total += given if given > 0 else 5 * (n + 1)
                n += 1
            bonus[asset_name(row["asset"])] = (total, n)
        return bonus

    # --- run economy ---

    def bot_level(self, li):
        return self.p["levels"][li].get("bot_level") or self.p["levels"][0]["bot_level"]

    def pooled_kills(self, level):
        """Kills per minute by rank on cleared waves of every difficulty, per unit of Count (seniors — of SeniorShare too):
        a winner kills all that comes, and the bot's deaths (fewer kills on the hard ones) don't count."""
        if level in self._pooled:
            return self._pooled[level]
        kills, minutes = defaultdict(float), 0.0
        for (lv, d, _), rows in self.bot.waves.items():
            if lv != level or d not in self.diff:
                continue
            for r in rows:
                if num(r["cleared"]) < 1:
                    continue
                minutes += num(r["duration_s"]) / 60
                for rank in RANKS:
                    scale = self.diff[d]["count"] * (self.diff[d]["seniorShare"] if rank != "Mass" else 1.0)
                    kills[rank] += num(r["kills_" + rank.lower()]) / scale
        pooled = {rank: kills[rank] / minutes for rank in RANKS} if minutes > 0 else None
        if pooled is None:
            self.note(f"no cleared waves for {level}: kills per minute of the runs as they are")
        self._pooled[level] = pooled
        return pooled

    def stats(self, li, d):
        """Bot statistics for the level's difficulty (kills — pooled cleared waves × Count, SeniorShare); no runs — the
        nearest difficulty."""
        level = self.bot_level(li)
        own = self.bot.summary(level, d)
        pooled = self.pooled_kills(level)
        if own and pooled:
            own = dict(own)
            for rank in RANKS:
                scale = self.diff[d]["count"] * (self.diff[d]["seniorShare"] if rank != "Mass" else 1.0)
                own["kpm_" + rank] = pooled[rank] * scale
        if own:
            return own
        for other in self.difficulties:
            s = self.bot.summary(level, other)
            if s:
                ratio = self.diff[d]["count"] / self.diff[other]["count"]
                s = dict(s)
                for rank in RANKS:
                    s["kpm_" + rank] *= ratio
                s["runs"] = 0
                self.note(f"no bot runs for {level} {d}: {other} scaled ×{ratio:.2f}")
                return s
        raise SystemExit("No bot runs in the batch: run Purge -> Balance -> Run Bot first.")

    def note(self, text):
        if text not in self.notes:
            self.notes.append(text)

    def minutes(self, li, di):
        return self.p["level_minutes"][di] * self.p["level_growth"]["minutes"] ** li

    def money_rate(self, li):
        return self.p["level_growth"].get("money", 1.0) ** li

    def income(self, li, di):
        """(run income, first clear bonus, breakdown) of a won run."""
        d = self.difficulties[di]
        s = self.stats(li, d)
        minutes = self.minutes(li, di)
        factor = self.diff[d]["money"] * self.money_rate(li)
        kills = sum(s["kpm_" + r] * self.p["rank_money"][r] for r in RANKS) * minutes * factor
        bonus, _ = self.wave_bonus.get("Forest_01", (0.0, 0))
        waves = bonus * factor
        other = max(0.0, s["other_pm"]) * minutes * self.money_rate(li)
        total = kills + waves + other
        return total, total * self.p["first_clear_bonus"], (kills, waves, other)

    def need(self, li, di):
        d, easy = self.diff[self.difficulties[di]], self.diff[self.difficulties[0]]
        return (d["health"] * d["count"]) / (easy["health"] * easy["count"]) * self.p["level_growth"]["health"] ** li

    # --- prices ---

    def weapon_price(self, rarity):
        return nice(self.price_one * self.p["price_ratio"][RARITIES.index(rarity)], self.p["price_round"])

    def upgrade_cost(self, rarity, level, base=None):
        """Cost of going to `level` (2..max)."""
        base = base if base is not None else self.weapon_price(rarity)
        return nice(self.scale * base * self.p["upgrade_growth"] ** (level - 2), self.p["price_round"])

    def upgrade_power(self, level):
        top = self.p["upgrade_levels"]
        return self.p["upgrade_power"] ** ((level - 1) / (top - 1))

    def throwable_price(self, rarity):
        return nice(self.weapon_price(rarity) * self.p["throwables"]["price_share"], self.p["price_round"])

    def node_cost(self, index):
        t = self.p["tree"]
        return nice(self.scale * t["node_base"] * self.price_one * t["node_growth"] ** index, self.p["price_round"])

    def trunk_cost(self, rarity):
        return nice(self.weapon_price(rarity) * self.p["tree"]["trunk_price_share"], self.p["price_round"])

    def weapons(self):
        unique = set(self.p["unique"])
        out = []
        for rarity in RARITIES:
            for name in self.p["rarities"][rarity]:
                start = rarity == "Common" or name in self.p["start_pool_extra"]
                out.append((name, rarity, name in unique, start))
        return out

    # --- progression ---

    def simulate(self, log=None):
        """A typical player: shops greedily, takes the easiest new clear it can win, else farms the best clear."""
        p = self.p
        weapons = self.weapons()
        rarity_of = {w[0]: w[1] for w in weapons}
        owned = {w[0] for w in weapons if w[3] and not w[2]}
        unique_at = {(self.levels.index(lv), self.difficulties.index(df)): name for name, (lv, df) in p["unique"].items()
                     if lv in self.levels}
        throwables = dict(p["throwables"]["rarity"])
        owned_throw = set(p["throwables"]["start"])
        throw_level = {name: 1 for name in throwables}
        upgrades = defaultdict(lambda: 1)
        trunk = 0  # nodes of the trunk bought (0 — only Common at start)
        trunk_nodes = p["tree"]["trunk"]
        branch_total = sum(len(v) for v in p["tree"]["branches"].values())
        branch = 0
        cleared = set()
        money, hours, runs = 0.0, 0.0, 0
        allowed = lambda: ["Common"] + [r for r, _ in trunk_nodes[:trunk]]
        pairs = [(li, di) for li in range(len(self.levels)) for di in range(len(self.difficulties))]
        incomes = {pair: self.income(*pair) for pair in pairs}
        farm_better = 0
        farm_cases = []

        def power():
            ok = allowed()
            best = max((p["rarity_power"] ** RARITIES.index(rarity_of[w]) * self.upgrade_power(upgrades[w])
                        for w in owned if rarity_of[w] in ok), default=1.0)
            return best * (1 + p["tree"]["node_power"] * branch)

        def openable():
            out = []
            for li, di in pairs:
                if (li, di) in cleared:
                    continue
                level_open = li == 0 or any((li - 1, d) in cleared for d in range(len(self.difficulties)))
                if level_open and (di == 0 or (li, di - 1) in cleared):
                    out.append((li, di))
            return out

        def shop():
            nonlocal money, trunk, branch
            bought = []
            while True:
                blocked = any(self.need(*pair) > power() for pair in openable()) or not openable()
                options = []  # (cost, power gain, label, apply); a new rarity is valued by its potential (fully upgraded)
                ok = allowed()
                # the main weapon — the rarest allowed one (it pays off upgrades), the most upgraded of them
                main = max((w for w in owned if rarity_of[w] in ok),
                           key=lambda w: (RARITIES.index(rarity_of[w]), upgrades[w]))
                main_rarity = RARITIES.index(rarity_of[main])
                if upgrades[main] < p["upgrade_levels"]:
                    options.append((self.upgrade_cost(rarity_of[main], upgrades[main] + 1),
                                    self.upgrade_power(upgrades[main] + 1) / self.upgrade_power(upgrades[main]),
                                    f"upgrade {main} {upgrades[main] + 1}", ("up", main)))
                for name, rarity, unique, _ in weapons:
                    if name in owned or unique:
                        continue
                    step = RARITIES.index(rarity) - main_rarity
                    gain = p["rarity_power"] ** step if rarity in ok and step > 0 else 1.0
                    options.append((self.weapon_price(rarity), gain, f"buy {name}", ("buy", name)))
                if trunk < len(trunk_nodes) and branch >= trunk_nodes[trunk][1]:
                    rarity = trunk_nodes[trunk][0]
                    step = RARITIES.index(rarity) - main_rarity
                    have = any(rarity_of[w] == rarity for w in owned)
                    cost = self.trunk_cost(rarity)
                    rank = cost if have else cost + self.weapon_price(rarity)  # the weapon is bought next
                    options.append((cost, p["rarity_power"] ** step if step > 0 else 1.0, f"trunk {rarity}", ("trunk", None), rank))
                if branch < branch_total:
                    options.append((self.node_cost(branch), 1 + p["tree"]["node_power"] / (1 + p["tree"]["node_power"] * branch),
                                    f"node {branch + 1}", ("node", None)))
                for name, rarity in throwables.items():
                    if name not in owned_throw:
                        options.append((self.throwable_price(rarity), 1.0, f"buy {name}", ("throw", name)))
                    elif throw_level[name] < p["throwables"]["levels"]:
                        options.append((self.upgrade_cost(rarity, throw_level[name] + 1, self.throwable_price(rarity)), 1.0,
                                        f"upgrade {name} {throw_level[name] + 1}", ("throwup", name)))
                if not options:
                    return bought
                if blocked:
                    useful = [o for o in options if o[1] > 1.0001]
                    pick = (max(useful, key=lambda o: math.log(o[1]) / (o[4] if len(o) > 4 else o[0])) if useful
                            else min(options, key=lambda o: o[0]))
                else:
                    pick = min(options, key=lambda o: o[0])
                if pick[0] > money:
                    return bought
                money -= pick[0]
                kind, name = pick[3]
                if kind == "up":
                    upgrades[name] += 1
                elif kind == "buy":
                    owned.add(name)
                elif kind == "trunk":
                    trunk += 1
                elif kind == "node":
                    branch += 1
                elif kind == "throw":
                    owned_throw.add(name)
                elif kind == "throwup":
                    throw_level[name] += 1
                bought.append(pick[2])

        def everything_open():
            buyable = all(w[0] in owned for w in weapons)
            return (buyable and len(owned_throw) == len(throwables) and trunk == len(trunk_nodes)
                    and branch == branch_total and len(cleared) == len(pairs))

        first_purchase_run = None
        while runs < 5000 and not everything_open():
            targets = openable()
            target = min(targets, key=lambda pair: self.need(*pair)) if targets else None
            farm = max(cleared, key=lambda pair: incomes[pair][0] / self.minutes(*pair), default=None)
            if target is not None and self.need(*target) <= power():
                pair, won = target, True
            elif farm is not None:
                pair, won = farm, True
            else:
                pair, won = target, False
            li, di = pair
            run, bonus, _ = incomes[pair]
            gain = run + (bonus if won and pair not in cleared else 0.0)
            if not won:
                gain = run * p["loss_income_share"]
            # Forward is better (card 10v): the new clear with its bonus per minute — not less than the best farm.
            if target is not None and pair == target and farm is not None:
                if (run + bonus) / self.minutes(*target) < incomes[farm][0] / self.minutes(*farm):
                    farm_better += 1
                    farm_cases.append(f"{self.levels[li]} {self.difficulties[di]} < {self.levels[farm[0]]} {self.difficulties[farm[1]]}")
            if won and pair not in cleared:
                cleared.add(pair)
                if pair in unique_at:
                    owned.add(unique_at[pair])
            money += gain
            minutes = self.minutes(*pair) * (1.0 if won else p["loss_income_share"])
            hours += minutes * p["play_overhead"] / 60
            runs += 1
            bought = shop()
            if first_purchase_run is None and any(b.startswith("buy ") for b in bought):
                first_purchase_run = runs
            if log is not None:
                log.append([runs, round(hours, 2), self.levels[li], self.difficulties[di], "won" if won else "lost",
                            round(gain), round(money), round(power(), 2), "; ".join(bought)])
        return {
            "hours": hours, "runs": runs, "done": everything_open(), "first_purchase_run": first_purchase_run,
            "farm_better": farm_better, "farm_cases": farm_cases, "power": power(),
        }

    def calibrate(self):
        """Meta scale (upgrades and tree branch nodes) so that everything opens in target_hours."""
        lo, hi = 0.01, 100.0
        for _ in range(40):
            self.scale = math.sqrt(lo * hi)
            result = self.simulate()
            if result["hours"] < self.p["target_hours"]:
                lo = self.scale
            else:
                hi = self.scale
        self.scale = math.sqrt(lo * hi)
        result = self.simulate()
        target = self.p["target_hours"]
        if result["hours"] > target * 1.05:
            self.note(f"{target} h is out of reach: with nearly free upgrades and tree it still takes {result['hours']:.1f} h — "
                      "weapon prices (anchor × price_ratio) alone are too high for the income; levers: first_clear_bonus, "
                      "level_growth.money, rank_money vs the anchor")
        elif result["hours"] < target * 0.95:
            self.note(f"{target} h is out of reach: even upgrades ×{self.scale:.0f} open everything in {result['hours']:.1f} h")
        return result

    # --- experience curve ---

    def card_targets(self, seconds):
        p = self.p["cards"]
        times, t = [], 0.0
        while True:
            t += p["interval_start"] + (p["interval_end"] - p["interval_start"]) * min(1.0, t / seconds)
            if t > seconds:
                return times
            times.append(t)

    def fit_experience(self):
        d = self.p["cards"]["difficulty"]
        di = self.difficulties.index(d)
        seconds = self.p["level_minutes"][di] * 60
        curves = self.bot.xp_curve(self.bot_level(0), d)
        targets = self.card_targets(seconds)
        if not curves:
            s = self.stats(0, d)
            curves = [[(0.0, 0.0), (seconds, s["xp_pm"] * seconds / 60)]]
            self.note("no wave rows for the experience curve: a constant rate from runs.csv")
        xp = [sum(interpolate(c, t) for c in curves) / len(curves) for t in targets]
        best = None
        for step in range(0, 121):
            g = 1.0 + step * 0.005
            sums = [(n if g == 1.0 else (g ** n - 1) / (g - 1)) for n in range(1, len(targets) + 1)]
            logs = [math.log(max(x, 1e-6)) - math.log(s) for x, s in zip(xp, sums)]
            first = math.exp(sum(logs) / len(logs))
            error = sum((l - math.log(first)) ** 2 for l in logs)
            if best is None or error < best[0]:
                best = (error, first, g)
        _, first, g = best
        first = max(1, round(first))
        rows, total = [], 0.0
        for n in range(1, self.p["cards"]["max_levels"] + 1):
            need = round(first * g ** (n - 1))
            total += need
            rows.append([n, need, total, targets[n - 1] if n <= len(targets) else ""])
        return first, round(g, 3), rows, targets, xp


# ---------- report ----------

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--batch", help="bot batch folder (default — params bot_batch, 'latest')")
    args = parser.parse_args()
    params = json.load(open(os.path.join(HERE, "params.json"), encoding="utf-8"))
    batch = args.batch or params.get("bot_batch", "latest")
    batch = latest_batch(os.path.join(HERE, "Bot")) if batch == "latest" else os.path.join(HERE, batch)
    bot = BotStats(batch)
    model = Model(params, os.path.join(HERE, "Data"), bot)
    out = os.path.join(HERE, "Model")
    os.makedirs(out, exist_ok=True)

    # anchor: a gray weapon = the first won Forest Easy run (with the first clear bonus)
    run, bonus, _ = model.income(0, 0)
    model.price_one = run + bonus
    result = model.calibrate()

    economy = []
    for li, level in enumerate(model.levels):
        for di, d in enumerate(model.difficulties):
            run, bonus, (kills, waves, other) = model.income(li, di)
            s = model.stats(li, d)
            economy.append([level, d, model.minutes(li, di), round(run), round(bonus), round(run / model.minutes(li, di), 1),
                            round(kills), round(waves), round(other), round(model.need(li, di), 2),
                            s["runs"] if li == 0 or params["levels"][li].get("bot_level") else 0])
    write_csv(os.path.join(out, "economy.csv"),
              ["level", "difficulty", "minutes", "run_income", "first_clear_bonus", "income_per_min",
               "kill_money", "wave_money", "other_money", "power_needed", "bot_runs"], economy)

    write_csv(os.path.join(out, "prices.csv"), ["weapon", "rarity", "price", "unique", "start_pool", "dps_factor"],
              [[n, r, "" if u else model.weapon_price(r), int(u), int(s), round(params["rarity_power"] ** RARITIES.index(r), 2)]
               for n, r, u, s in model.weapons()])

    upgrades = []
    for rarity in RARITIES:
        total = 0
        for level in range(2, params["upgrade_levels"] + 1):
            cost = model.upgrade_cost(rarity, level)
            total += cost
            fork = int(level in params["fork_levels"])
            upgrades.append([rarity, level, cost, total, round(model.upgrade_power(level), 3), fork,
                             nice(cost * params["fork_change_share"], 1) if fork else ""])
    write_csv(os.path.join(out, "upgrades.csv"),
              ["rarity", "level", "cost", "total", "dps_factor", "fork", "fork_change"], upgrades)

    throw_rows = []
    for name, rarity in params["throwables"]["rarity"].items():
        price = model.throwable_price(rarity)
        start = name in params["throwables"]["start"]
        throw_rows.append([name, rarity, "" if start else price] +
                          [model.upgrade_cost(rarity, lv, price) for lv in range(2, params["throwables"]["levels"] + 1)])
    write_csv(os.path.join(out, "throwables.csv"),
              ["throwable", "rarity", "price"] + [f"level{lv}" for lv in range(2, params["throwables"]["levels"] + 1)], throw_rows)

    tree, index = [], 0
    for rarity, nodes in params["tree"]["trunk"]:
        tree.append(["Trunk", f"Allow {rarity}", model.trunk_cost(rarity), f"{nodes} branch nodes"])
    branches = params["tree"]["branches"]
    order = []
    depth = max(len(v) for v in branches.values())
    for i in range(depth):  # the model buys branches round-robin: node cost grows with the count bought
        for branch, nodes in branches.items():
            if i < len(nodes):
                order.append((branch, nodes[i]))
    for branch, node in order:
        tree.append([branch, node, model.node_cost(index), f"{index + 1}th node bought"])
        index += 1
    write_csv(os.path.join(out, "tree.csv"), ["branch", "node", "cost", "note"], tree)

    boosters = []
    for li, level in enumerate(model.levels):
        for di, d in enumerate(model.difficulties):
            run, _, _ = model.income(li, di)
            row = [level, d]
            for name, weight in params["boosters"]["weights"].items():
                row.append(nice(run * params["boosters"]["income_share"] * weight, 5))
            boosters.append(row)
    write_csv(os.path.join(out, "boosters.csv"), ["level", "difficulty"] + list(params["boosters"]["weights"]), boosters)

    first, growth, xp_rows, targets, xp = model.fit_experience()
    write_csv(os.path.join(out, "experience.csv"), ["card_level", "experience", "total", "target_second"], xp_rows)

    log = []
    model.simulate(log)
    write_csv(os.path.join(out, "progression.csv"),
              ["run", "hours", "level", "difficulty", "outcome", "income", "money", "power", "bought"], log)

    write_report(out, model, bot, result, economy, first, growth, targets, xp)
    sys.stdout.reconfigure(encoding="utf-8")
    print(open(os.path.join(out, "report.md"), encoding="utf-8").read())


def write_report(out, model, bot, result, economy, first, growth, targets, xp):
    p = model.p
    lines = ["# Economy model — report", ""]
    lines.append(f"- Bot batch: `{os.path.relpath(bot.batch, HERE) if bot.batch else '—'}`.")
    lines.append(f"- Gray weapon = first won Forest Easy run (with first clear bonus): **{model.weapon_price('Common')}**; "
                 f"prices by rarity: " + " / ".join(str(model.weapon_price(r)) for r in RARITIES) + ".")
    lines.append(f"- Meta scale (upgrades, tree nodes) calibrated to {p['target_hours']} h: **{model.scale:.3f}** → "
                 f"everything open in **{result['hours']:.1f} h**, {result['runs']} runs"
                 + ("" if result["done"] else " (**not reached** — run cap)") + ".")
    lines.append(f"- First weapon bought after run **{result['first_purchase_run']}** (target — 1).")
    full = sum(model.upgrade_cost("Common", lv) for lv in range(2, p["upgrade_levels"] + 1))
    lines.append(f"- Upgrade 1→{p['upgrade_levels']} costs **{full / model.weapon_price('Common'):.2f}×** the weapon's price "
                 f"(gray: {full}); a tree node {model.node_cost(0)}–{model.node_cost(sum(len(v) for v in p['tree']['branches'].values()) - 1)}.")
    lines.append(f"- Simulated player wins the last clear at power **{result['power']:.2f}** (gray, no upgrades = 1).")
    lines.append(f"- New clear paid less per minute than the best farm in **{result['farm_better']}** cases (target — 0: forward is better).")
    if result.get("farm_cases"):
        lines.append("  - " + "; ".join(sorted(set(result["farm_cases"]))) + ".")
    lines.append(f"- Experience curve: `CardDeck._firstLevel` **{first}**, `_growth` **{growth}** "
                 f"({len(targets)} cards in {p['level_minutes'][model.difficulties.index(p['cards']['difficulty'])]} min on "
                 f"{p['cards']['difficulty']}, every {p['cards']['interval_start']}–{p['cards']['interval_end']} s).")
    lines.append("")
    lines.append("## Bot vs targets (Forest)")
    lines.append("")
    lines.append("| Difficulty | Runs | Won | Minutes (bot) | Target | Waves cleared | Mass/min | Officer/min | Elite/min | Boss/min | Damage taken/min |")
    lines.append("|---|---|---|---|---|---|---|---|---|---|---|")
    for di, d in enumerate(model.difficulties):
        s = bot.summary(model.bot_level(0), d)
        if not s:
            lines.append(f"| {d} | 0 | | | {p['level_minutes'][di]} | | | | | | |")
            continue
        lines.append(f"| {d} | {s['runs']} | {s['won']} | {s['minutes']:.1f} | {p['level_minutes'][di]} | {s['waves']:.1f} | "
                     f"{s['kpm_Mass']:.1f} | {s['kpm_Officer']:.2f} | {s['kpm_Elite']:.2f} | {s['kpm_Boss']:.2f} | {s['damage_pm']:.0f} |")
    lines.append("")
    lines.append("## Income per run")
    lines.append("")
    lines.append("| Level | Difficulty | Minutes | Run | First clear | Per min | Power needed |")
    lines.append("|---|---|---|---|---|---|---|")
    for row in economy:
        lines.append(f"| {row[0]} | {row[1]} | {row[2]:.1f} | {row[3]} | {row[4]} | {row[5]} | {row[9]} |")
    lines.append("")
    if model.notes:
        lines.append("## Notes")
        lines.append("")
        lines.extend("- " + n for n in model.notes)
        lines.append("")
    lines.append("Tables: `economy.csv`, `prices.csv`, `upgrades.csv`, `throwables.csv`, `tree.csv`, `boosters.csv`, "
                 "`experience.csv`, `progression.csv` (the simulated player, run by run).")
    with open(os.path.join(out, "report.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    sys.exit(main())
