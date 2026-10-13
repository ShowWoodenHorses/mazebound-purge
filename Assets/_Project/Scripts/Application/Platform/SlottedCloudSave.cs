using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Maze.Application.Platform
{
    /// <summary>A site's key-value storage for the player (VK Storage): short string values only.</summary>
    public interface IKeyValueStorage
    {
        /// <summary>The values of the keys (a missing or empty key — null or empty). Throws when the site did not answer.</summary>
        UniTask<IReadOnlyDictionary<string, string>> GetAsync(IReadOnlyList<string> keys, CancellationToken cancellation);

        UniTask SetAsync(string key, string value, CancellationToken cancellation);
    }

    /// <summary>
    /// A save bigger than one value of a key-value storage (VK Storage keeps ≤ 4096 characters a key and silently cuts
    /// the rest): the JSON goes in parts of <see cref="PartSize"/> characters. Two slots, A and B: a write fills the
    /// slot not in use, then switches <see cref="PointerKey"/> to it (<c>"B:3"</c> — slot and part count). A write cut
    /// short (the tab closed) leaves the pointer on the old, whole save. An unchanged save is not written again (the
    /// storage limits calls per hour). Read before the first write: the slot in use is known only then
    /// (<see cref="Save.SaveService"/> writes only to a cloud that answered its read).
    /// </summary>
    public sealed class SlottedCloudSave : ICloudSave
    {
        public const string PointerKey = "save_slot";

        /// <summary>
        /// Characters a part. VK documents 4096 characters a value but also «2236 for a serialized string»: JSON quotes
        /// may be counted escaped, so parts stay well below both.
        /// </summary>
        public const int PartSize = 1000;

        private readonly IKeyValueStorage _storage;
        private readonly List<string> _keys = new List<string>();
        private char _slot; // in use; '\0' — nothing saved yet
        private string _written;

        public SlottedCloudSave(IKeyValueStorage storage)
        {
            _storage = storage;
        }

        public static string PartKey(char slot, int index) => "save_" + slot + index.ToString(CultureInfo.InvariantCulture);

        public async UniTask<string> ReadAsync(CancellationToken cancellation)
        {
            _keys.Clear();
            _keys.Add(PointerKey);
            var pointer = Value(await _storage.GetAsync(_keys, cancellation), PointerKey);
            if (string.IsNullOrEmpty(pointer))
            {
                _slot = '\0';
                return null;
            }

            if (!TryParsePointer(pointer, out var slot, out var count))
                throw new FormatException($"Cloud save pointer '{pointer}' is broken.");

            _keys.Clear();
            for (var i = 0; i < count; i++) _keys.Add(PartKey(slot, i));
            var values = await _storage.GetAsync(_keys, cancellation);
            var json = new StringBuilder(count * PartSize);
            foreach (var key in _keys)
            {
                var part = Value(values, key);
                if (string.IsNullOrEmpty(part)) throw new FormatException($"Cloud save part '{key}' is missing.");
                json.Append(part);
            }

            _slot = slot;
            _written = json.ToString();
            return _written;
        }

        public async UniTask WriteAsync(string json, CancellationToken cancellation)
        {
            json ??= "";
            if (json == _written) return;
            var slot = _slot == 'A' ? 'B' : 'A';
            var count = (json.Length + PartSize - 1) / PartSize;
            for (var i = 0; i < count; i++)
            {
                var start = i * PartSize;
                await _storage.SetAsync(PartKey(slot, i), json.Substring(start, Math.Min(PartSize, json.Length - start)),
                    cancellation);
            }

            await _storage.SetAsync(PointerKey, count == 0 ? "" : slot + ":" + count.ToString(CultureInfo.InvariantCulture),
                cancellation);
            _slot = count == 0 ? '\0' : slot;
            _written = json;
        }

        public static bool TryParsePointer(string pointer, out char slot, out int count)
        {
            slot = '\0';
            count = 0;
            if (pointer == null || pointer.Length < 3 || pointer[1] != ':') return false;
            slot = pointer[0];
            return (slot == 'A' || slot == 'B') &&
                   int.TryParse(pointer.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
                   count > 0;
        }

        private static string Value(IReadOnlyDictionary<string, string> values, string key) =>
            values != null && values.TryGetValue(key, out var value) ? value : null;
    }
}
