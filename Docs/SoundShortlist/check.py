# Проверка шортлиста после прослушивания.
#   python -I check.py            — отчёт: пункты, где звуков меньше / больше, чем нужно_N
#   python -I check.py --snapshot — сохранить снимок (snapshot.csv рядом со скриптом)
# Пункт = папка с файлом нужно_N.txt; звуки считаются вместе с подпапками-источниками (_добавлено_*, _PolygonArsenal, _ImportSounds).
import os, sys, csv

sys.stdout.reconfigure(encoding="utf-8")

S = r"C:\Users\Vladimir\For games\Sounds\Purge_Shortlist"
SNAP = os.path.join(os.path.dirname(os.path.abspath(__file__)), "snapshot.csv")
AUDIO = (".wav", ".mp3", ".ogg")

def leaves():
    for root, dirs, files in os.walk(S):
        dirs[:] = [d for d in dirs if d != "_вне_проекта"]
        need = [f for f in files if f.startswith("нужно_") and f.endswith(".txt")]
        if not need:
            continue
        n = int(need[0][len("нужно_"):-4])
        sounds = []
        for r, ds, fs in os.walk(root):
            if r != root and any(f.startswith("нужно_") for f in fs):
                continue  # вложенный пункт считается отдельно
            sounds += [os.path.relpath(os.path.join(r, f), S).replace(os.sep, "/") for f in fs if f.lower().endswith(AUDIO)]
        yield os.path.relpath(root, S).replace(os.sep, "/"), n, sorted(sounds)

def main():
    data = sorted(leaves())
    if "--snapshot" in sys.argv:
        with open(SNAP, "w", encoding="utf-8-sig", newline="") as fh:
            w = csv.writer(fh, delimiter=";")
            w.writerow(["Пункт", "Нужно", "Файл"])
            for leaf, n, sounds in data:
                for s in sounds or [""]:
                    w.writerow([leaf, n, s])
        print(f"снимок: {len(data)} пунктов, {sum(len(s) for _, _, s in data)} звуков → {SNAP}")
        return
    before = {}
    if os.path.exists(SNAP):
        with open(SNAP, encoding="utf-8-sig") as fh:
            for row in list(csv.reader(fh, delimiter=";"))[1:]:
                before.setdefault(row[0], set()).update([row[2]] if row[2] else [])
    less, more, ok = [], [], 0
    for leaf, n, sounds in data:
        removed = len(before.get(leaf, set()) - set(sounds))
        line = f"  {leaf}: есть {len(sounds)}, нужно {n}" + (f" (удалено {removed})" if removed else "")
        if len(sounds) < n: less.append(line)
        elif len(sounds) > n: more.append(line)
        else: ok += 1
    print(f"Пунктов: {len(data)}, ровно: {ok}")
    print(f"\nМЕНЬШЕ, чем нужно ({len(less)}):", *less, sep="\n")
    print(f"\nБОЛЬШЕ, чем нужно ({len(more)}):", *more, sep="\n")
    gone = sorted(set(before) - {l for l, _, _ in data})
    if gone:
        print("\nПункты из снимка, которых больше нет:", *gone, sep="\n  ")

if __name__ == "__main__":
    main()
