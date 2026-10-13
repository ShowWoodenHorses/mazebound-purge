// VK Bridge (Maze.Application.Platform.VkBridge). The page loads the library and sends VKWebAppInit before the game
// loads (WebPlatforms.Head). Async calls answer through one callback: (request id, code, text or 0). Id 0 — events
// from VK (code 1: the game is hidden, 2: shown again).
mergeInto(LibraryManager.library, {
  MazeVk_Init: function (callback, id) {
    var state = window.mazeVk = { callback: callback, bridge: null };
    state.reply = function (requestId, code, text) {
      var ptr = 0;
      if (text != null) {
        text = String(text);
        var size = lengthBytesUTF8(text) + 1;
        ptr = _malloc(size);
        stringToUTF8(text, ptr, size);
      }
      {{{ makeDynCall('viii', 'state.callback') }}}(requestId, code, ptr);
      if (ptr) _free(ptr);
    };
    state.error = function (e) {
      if (e && e.error_data) return JSON.stringify(e.error_data);
      try { return typeof e === 'object' ? JSON.stringify(e) : String(e); } catch (x) { return String(e); }
    };
    var bridge = window.vkBridge;
    if (!bridge) {
      state.reply(id, 0, 'VK Bridge is not loaded');
      return;
    }
    // A real call: outside VK it fails or never answers (the game's timeout), and the game goes on without VK.
    bridge.send('VKWebAppGetLaunchParams').then(function (params) {
      state.bridge = bridge;
      var query = new URLSearchParams(window.location.search);
      var language = (params && params.vk_language) || query.get('vk_language') || '';
      var platform = (params && params.vk_platform) || query.get('vk_platform') || '';
      // System bars over the game (the VK app on a phone): the game keeps its controls clear of them (MazePage.jslib).
      // The VK app on Android did not report its navigation bar (checked 2026-10-12): at least the bar's usual width
      // (48 CSS px) on both sides there — the bar moves to the other side when the phone is turned over.
      var phoneApp = platform === 'mobile_android' || platform === 'mobile_iphone' || platform === 'mobile_ipad';
      var sideMinimum = phoneApp ? 48 : 0;
      var insets = function (config) {
        if (!window.mazeSetSiteInsets) return;
        var reported = (config && config.insets) || {};
        window.mazeSetSiteInsets({
          left: Math.max(reported.left || 0, sideMinimum), top: reported.top || 0,
          right: Math.max(reported.right || 0, sideMinimum), bottom: reported.bottom || 0
        });
      };
      insets(null);
      bridge.subscribe(function (event) {
        var type = event && event.detail && event.detail.type;
        if (type === 'VKWebAppViewHide') state.reply(0, 1, null);
        else if (type === 'VKWebAppViewRestore') state.reply(0, 2, null);
        else if (type === 'VKWebAppUpdateConfig') insets(event.detail.data);
      });
      bridge.send('VKWebAppGetConfig').then(insets).catch(function () { });
      // Ads are fetched ahead (a show then starts at once); VK advises checking again on a timer.
      var preload = function () {
        bridge.send('VKWebAppCheckNativeAds', { ad_format: 'reward' }).catch(function () { });
        bridge.send('VKWebAppCheckNativeAds', { ad_format: 'interstitial' }).catch(function () { });
      };
      preload();
      setInterval(preload, 60000);
      state.reply(id, 1, language + ';' + platform);
    }).catch(function (e) {
      state.reply(id, 0, state.error(e));
    });
  },

  MazeVk_StorageGet: function (id, keysJson) {
    var state = window.mazeVk;
    var keys = JSON.parse(UTF8ToString(keysJson));
    state.bridge.send('VKWebAppStorageGet', { keys: keys })
      .then(function (data) { state.reply(id, 1, JSON.stringify({ keys: (data && data.keys) || [] })); })
      .catch(function (e) { state.reply(id, 0, state.error(e)); });
  },

  MazeVk_StorageSet: function (id, key, value) {
    var state = window.mazeVk;
    state.bridge.send('VKWebAppStorageSet', { key: UTF8ToString(key), value: UTF8ToString(value) })
      .then(function (data) { state.reply(id, data && data.result ? 1 : 0, null); })
      .catch(function (e) { state.reply(id, 0, state.error(e)); });
  },

  // Codes = Maze.Application.Platform.AdResult: 0 rewarded, 1 closed, 2 failed, 3 unavailable.
  MazeVk_ShowAd: function (id, rewarded) {
    var state = window.mazeVk;
    state.bridge.send('VKWebAppShowNativeAds', { ad_format: rewarded ? 'reward' : 'interstitial' })
      .then(function (data) {
        if (data && data.result) state.reply(id, rewarded ? 0 : 1, null);
        else state.reply(id, 3, 'not shown');
      })
      .catch(function (e) { state.reply(id, 2, state.error(e)); });
  },

  // The sticky banner at the bottom (VK draws it and shrinks the game's frame: layout 'resize'). Compact — the lower
  // banner of landscape games on phones (ignored on the desktop site).
  MazeVk_Banner: function (show, compact) {
    var state = window.mazeVk;
    if (!state || !state.bridge) return;
    var call = show
      ? state.bridge.send('VKWebAppShowBannerAd', {
          banner_location: 'bottom', layout_type: 'resize', height_type: compact ? 'compact' : 'regular'
        })
      : state.bridge.send('VKWebAppHideBannerAd');
    call.catch(function (e) { console.warn('[Maze] VK banner: ' + state.error(e)); });
  }
});
