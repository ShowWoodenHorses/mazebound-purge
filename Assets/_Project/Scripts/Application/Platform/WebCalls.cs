using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Maze.Application.Platform
{
    /// <summary>The answer of a site's SDK to one call: a code (meaning depends on the call) and an optional text.</summary>
    public readonly struct WebReply
    {
        public readonly int Code;
        public readonly string Text;

        public WebReply(int code, string text)
        {
            Code = code;
            Text = text;
        }
    }

    /// <summary>
    /// Async calls into a site's SDK through a <c>.jslib</c> (<see cref="YandexBridge"/>, <see cref="CrazyBridge"/>): every
    /// call gets a request id, the page answers once with (id, code, text) through the bridge's callback. Id 0 is left for
    /// events the site sends by itself.
    /// </summary>
    public sealed class WebCalls
    {
        private readonly Dictionary<int, UniTaskCompletionSource<WebReply>> _pending =
            new Dictionary<int, UniTaskCompletionSource<WebReply>>();

        private int _nextId = 1;

        /// <param name="start">Starts the call in the page with the given id; may answer at once.</param>
        public UniTask<WebReply> Call(Action<int> start, CancellationToken cancellation)
        {
            var id = _nextId++;
            var source = new UniTaskCompletionSource<WebReply>();
            _pending[id] = source;
            try
            {
                start(id);
            }
            catch
            {
                _pending.Remove(id);
                throw;
            }

            return source.Task.AttachExternalCancellation(cancellation);
        }

        /// <summary>The page's answer to a call (a call cancelled meanwhile is just forgotten).</summary>
        public void Complete(int id, int code, string text)
        {
            if (!_pending.TryGetValue(id, out var source)) return;
            _pending.Remove(id);
            source.TrySetResult(new WebReply(code, text));
        }

        /// <summary>A UTF-8 string the page passed (freed by the page after the callback returns).</summary>
        public static string Text(IntPtr text) => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);
    }
}
