namespace Maze.Editor.Build
{
    /// <summary>The game site a WebGL build is made for (Docs/Progress.md «Площадки публикации»).</summary>
    internal enum WebPlatform
    {
        /// <summary>No site: a local test build, any static server.</summary>
        Generic = 0,
        Yandex = 1,
        CrazyGames = 2,
        /// <summary>VK Games: vk.com, m.vk.com, the VK apps (and later Odnoklassniki).</summary>
        Vk = 3,
        /// <summary>GameDistribution: GD hosts the game and spreads it over thousands of game sites.</summary>
        GameDistribution = 4,
    }

    internal static class WebPlatforms
    {
        /// <summary>
        /// The scripting symbol of the site's build: <c>ProjectLifetimeScope</c> registers that site's
        /// <c>IGamePlatform</c>, ads and analytics under it. Null — no symbol (Generic).
        /// </summary>
        public static string Define(WebPlatform platform) => platform switch
        {
            WebPlatform.Yandex => "MAZE_YANDEX",
            WebPlatform.CrazyGames => "MAZE_CRAZYGAMES",
            WebPlatform.Vk => "MAZE_VK",
            WebPlatform.GameDistribution => "MAZE_GAMEDISTRIBUTION",
            _ => null,
        };

        /// <summary>The build folder in <c>Builds</c>: WebGL, WebGL-dev, WebGL-Yandex, WebGL-Yandex-dev…</summary>
        public static string Folder(WebPlatform platform, bool development) =>
            "WebGL" + (platform == WebPlatform.Generic ? "" : "-" + platform) + (development ? "-dev" : "");

        /// <summary>
        /// The Yandex Metrica counter of the game (metrika.yandex.ru, linked to the game in the Yandex Games console).
        /// Empty — no counter: events only go to the log. The one place to set it; rebuild after a change.
        /// </summary>
        public const string YandexMetrikaCounter = "";

        /// <summary>
        /// HTML put into the page's &lt;head&gt; (the template's <c>{{{ MAZE_PLATFORM_HEAD }}}</c>): the site's SDK
        /// script (and the Metrica counter on Yandex Games).
        /// </summary>
        public static string Head(WebPlatform platform) => platform switch
        {
            // The SDK is served by Yandex Games next to the game (and by sdk-dev-proxy locally).
            WebPlatform.Yandex => "<script src=\"/sdk.js\"></script>" + YandexMetrika(YandexMetrikaCounter),
            // HTML5 SDK v3; on localhost it runs in its local mode (demo ads, a test user).
            WebPlatform.CrazyGames => "<script src=\"https://sdk.crazygames.com/crazygames-sdk-v3.js\"></script>",
            // VK Bridge next to the page (a CDN may be blocked in a player's region); VK wants VKWebAppInit before the
            // game's resources load (within 30 s).
            WebPlatform.Vk => "<script src=\"" + VkBridgeFile + "\"></script>\n    " +
                              "<script>vkBridge.send('VKWebAppInit').catch(function (e) { console.warn('[Maze] VKWebAppInit', e); });</script>",
            WebPlatform.GameDistribution => GameDistributionHead(GameDistributionGameId),
            _ => "",
        };

        /// <summary>The game's id in the GameDistribution developer panel (Upload tab). Rebuild after a change.</summary>
        public const string GameDistributionGameId = "738b7f3f16bf4176bb70e01f8d655802";

        /// <summary>
        /// GD's loader, as in its docs: <c>GD_OPTIONS</c>, then the SDK script once, while the game loads (not later —
        /// GD rule). <c>onEvent</c> keeps SDK_READY / SDK_ERROR in <c>window.mazeGd</c> for the game (it may come before
        /// the game starts) and passes every event to the game's listener (<c>MazeGd.jslib</c>).
        /// </summary>
        private static string GameDistributionHead(string gameId) =>
            "<script>\n" +
            "      window.mazeGd = { ready: false, error: null, listener: null };\n" +
            "      window.GD_OPTIONS = {\n" +
            $"        gameId: '{gameId.Trim()}',\n" +
            "        advertisementSettings: { autoplay: false },\n" +
            "        onEvent: function (event) {\n" +
            "          var gd = window.mazeGd;\n" +
            "          if (event.name === 'SDK_READY') gd.ready = true;\n" +
            "          else if (event.name === 'SDK_ERROR') gd.error = event.message || 'SDK_ERROR';\n" +
            "          if (gd.listener) gd.listener(event);\n" +
            "        }\n" +
            "      };\n" +
            "      (function (d, s, id) {\n" +
            "        var js, fjs = d.getElementsByTagName(s)[0];\n" +
            "        if (d.getElementById(id)) return;\n" +
            "        js = d.createElement(s); js.id = id;\n" +
            "        js.src = 'https://html5.api.gamedistribution.com/main.min.js';\n" +
            "        fjs.parentNode.insertBefore(js, fjs);\n" +
            "      }(document, 'script', 'gamedistribution-jssdk'));\n" +
            "    </script>";

        /// <summary>The standard Metrica counter snippet; <c>window.mazeMetrika</c> tells the game its id.</summary>
        private static string YandexMetrika(string counter)
        {
            if (string.IsNullOrWhiteSpace(counter)) return "";
            counter = counter.Trim();
            return "\n    <script>(function(m,e,t,r,i,k,a){m[i]=m[i]||function(){(m[i].a=m[i].a||[]).push(arguments)};" +
                   "m[i].l=1*new Date();for(var j=0;j<document.scripts.length;j++){if(document.scripts[j].src===r){return;}}" +
                   "k=e.createElement(t),a=e.getElementsByTagName(t)[0],k.async=1,k.src=r,a.parentNode.insertBefore(k,a)})" +
                   "(window,document,\"script\",\"https://mc.yandex.ru/metrika/tag.js\",\"ym\");" +
                   $"window.mazeMetrika={counter};ym({counter},\"init\",{{clickmap:false,trackLinks:false,accurateTrackBounce:true}});</script>";
        }

        /// <summary>
        /// The VK Games app id (dev.vk.com → the game's settings). The upload config (<c>Builds/vk-hosting-config.json</c>)
        /// takes it; empty — the config is written without it (fill it in, or set MINI_APPS_APP_ID).
        /// </summary>
        public const string VkAppId = "54818441";

        /// <summary>VK Bridge 3.0.2 (MIT, npm @vkontakte/vk-bridge, dist/browser.min.js): copied next to the VK build's page.</summary>
        public const string VkBridgeSource = "WebPlatforms/Vk/vk-bridge.min.js";

        public const string VkBridgeFile = "vk-bridge.min.js";

        /// <summary>
        /// The site takes the build as a zip archive (index.html at its root): Yandex Games, CrazyGames (HTML5),
        /// GameDistribution.
        /// </summary>
        public static bool NeedsArchive(WebPlatform platform) =>
            platform == WebPlatform.Yandex || platform == WebPlatform.CrazyGames || platform == WebPlatform.GameDistribution;
    }
}
