using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Application.Save;

namespace Maze.Application.Platform
{
    /// <summary>
    /// Signing in to the site's account (Yandex Games): only when the player asks for it (the site's rules — never
    /// forced). After a sign-in the account's cloud save and the current one are merged
    /// (<see cref="SaveService.MergeCloudAsync"/>): nothing played before signing in is lost.
    /// </summary>
    public sealed class AccountService
    {
        private readonly PlatformService _platform;
        private readonly SaveService _save;
        private readonly AnalyticsService _analytics;

        public AccountService(PlatformService platform, SaveService save, AnalyticsService analytics = null)
        {
            _analytics = analytics;
            _platform = platform;
            _save = save;
            _platform.SignedIn += OnSignedInElsewhere;
        }

        /// <summary>The sign-in offer makes sense now: the site has accounts, the player is not signed in, no sign-in runs.</summary>
        public bool CanSignIn => !IsBusy && _platform.CanSignIn;

        public bool IsBusy { get; private set; }

        /// <summary>A sign-in started or ended (the offer may disappear, the progress may have grown).</summary>
        public event Action Changed;

        public async UniTask<bool> SignInAsync(CancellationToken cancellation)
        {
            if (!CanSignIn) return false;
            IsBusy = true;
            Changed?.Invoke();
            var signedIn = false;
            try
            {
                signedIn = await _platform.SignInAsync(cancellation);
                if (signedIn) await _save.MergeCloudAsync(cancellation);
            }
            finally
            {
                IsBusy = false;
                if (_analytics != null)
                {
                    _analytics.Begin()["result"] = signedIn ? "signed_in" : "cancelled";
                    _analytics.Send(AnalyticsEvents.SignIn);
                }

                Changed?.Invoke();
            }

            return signedIn;
        }

        /// <summary>Signed in through the site itself: the account's save joins the current one as after the button.</summary>
        private void OnSignedInElsewhere()
        {
            if (!IsBusy) MergeAsync().Forget();
        }

        private async UniTaskVoid MergeAsync()
        {
            IsBusy = true;
            Changed?.Invoke();
            try
            {
                await _save.MergeCloudAsync(CancellationToken.None);
            }
            finally
            {
                IsBusy = false;
                Changed?.Invoke();
            }
        }
    }
}
