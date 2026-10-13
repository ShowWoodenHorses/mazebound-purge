// Yandex Games SDK v2 (Maze.Application.Platform.Yandex.YandexBridge). The page loads /sdk.js (WebPlatforms.Head).
// Async calls answer through one callback: (request id, code, text or 0). Id 0 — events from the site
// (code 1: pause, 2: resume, 3: an ad started).
mergeInto(LibraryManager.library, {
  MazeYandex_Init: function (callback, id) {
    var state = window.mazeYandex = { callback: callback, ysdk: null, player: null };
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
    state.isAuthorized = function () {
      var player = state.player;
      if (!player) return 0;
      if (typeof player.isAuthorized === 'function') return player.isAuthorized() ? 1 : 0;
      return player.getMode() === 'lite' ? 0 : 1;
    };
    state.loadPlayer = function () {
      return state.ysdk.getPlayer({ scopes: false })
        .then(function (player) { state.player = player; })
        .catch(function (e) { console.warn('[Maze] Yandex player is not available:', e); });
    };
    if (typeof YaGames === 'undefined') {
      state.reply(id, 0, 'The Yandex Games SDK (/sdk.js) is not loaded');
      return;
    }
    YaGames.init().then(function (ysdk) {
      state.ysdk = ysdk;
      ysdk.on('game_api_pause', function () { state.reply(0, 1, null); });
      ysdk.on('game_api_resume', function () { state.reply(0, 2, null); });
      return state.loadPlayer();
    }).then(function () {
      var lang = state.ysdk.environment && state.ysdk.environment.i18n ? state.ysdk.environment.i18n.lang : '';
      state.reply(id, 1, lang || '');
    }).catch(function (e) {
      state.reply(id, 0, String(e));
    });
  },

  MazeYandex_HasPlayer: function () {
    return window.mazeYandex && window.mazeYandex.player ? 1 : 0;
  },

  MazeYandex_IsAuthorized: function () {
    return window.mazeYandex ? window.mazeYandex.isAuthorized() : 0;
  },

  MazeYandex_SignIn: function (id) {
    var state = window.mazeYandex;
    state.ysdk.auth.openAuthDialog()
      .then(function () { return state.loadPlayer(); })
      .then(function () { state.reply(id, state.isAuthorized(), null); })
      .catch(function () { state.reply(id, 0, null); }); // closed by the player
  },

  MazeYandex_GetData: function (id) {
    var state = window.mazeYandex;
    state.player.getData(['save'])
      .then(function (data) { state.reply(id, 1, data && typeof data.save === 'string' ? data.save : ''); })
      .catch(function (e) { state.reply(id, 0, String(e)); });
  },

  MazeYandex_SetData: function (id, json) {
    var state = window.mazeYandex;
    state.player.setData({ save: UTF8ToString(json) }, true)
      .then(function () { state.reply(id, 1, null); })
      .catch(function (e) { state.reply(id, 0, String(e)); });
  },

  // Codes = Maze.Application.Platform.AdResult: 0 rewarded, 1 closed, 2 failed, 3 unavailable.
  MazeYandex_ShowFullscreen: function (id) {
    var state = window.mazeYandex;
    var done = false;
    function end(code, text) { if (!done) { done = true; state.reply(id, code, text); } }
    state.ysdk.adv.showFullscreenAdv({ callbacks: {
      onOpen: function () { state.reply(0, 3, null); },
      onClose: function (wasShown) { end(wasShown ? 1 : 3, null); },
      onError: function (e) { end(2, String(e)); },
      onOffline: function () { end(2, 'offline'); }
    } });
  },

  MazeYandex_ShowRewarded: function (id) {
    var state = window.mazeYandex;
    var done = false;
    var rewarded = false;
    function end(code, text) { if (!done) { done = true; state.reply(id, code, text); } }
    state.ysdk.adv.showRewardedVideo({ callbacks: {
      onOpen: function () { state.reply(0, 3, null); },
      onRewarded: function () { rewarded = true; },
      onClose: function () { end(rewarded ? 0 : 1, null); },
      onError: function (e) { end(2, String(e)); }
    } });
  },

  MazeYandex_LoadingReady: function () {
    var ysdk = window.mazeYandex && window.mazeYandex.ysdk;
    if (ysdk && ysdk.features.LoadingAPI) ysdk.features.LoadingAPI.ready();
  },

  MazeYandex_Gameplay: function (start) {
    var ysdk = window.mazeYandex && window.mazeYandex.ysdk;
    if (!ysdk || !ysdk.features.GameplayAPI) return;
    if (start) ysdk.features.GameplayAPI.start();
    else ysdk.features.GameplayAPI.stop();
  },

  // Yandex Metrica goal; nothing without the counter (window.mazeMetrika is set by the page's counter snippet).
  MazeYandex_Goal: function (name, parameters) {
    if (typeof ym !== 'function' || !window.mazeMetrika) return;
    var params;
    try { params = JSON.parse(UTF8ToString(parameters)); } catch (e) { params = undefined; }
    ym(window.mazeMetrika, 'reachGoal', UTF8ToString(name), params);
  }
});
