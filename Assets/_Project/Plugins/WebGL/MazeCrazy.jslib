// CrazyGames HTML5 SDK v3 (Maze.Application.Platform.CrazyBridge). The page loads the SDK script (WebPlatforms.Head).
// Async calls answer through one callback: (request id, code, text or 0). Id 0 — events from the site
// (code 3: an ad started, 4: mute on, 5: mute off, 6: signed in through the site).
mergeInto(LibraryManager.library, {
  MazeCrazy_Init: function (callback, id) {
    var state = window.mazeCrazy = { callback: callback, sdk: null, user: null };
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
    if (!window.CrazyGames || !window.CrazyGames.SDK) {
      state.reply(id, 0, 'The CrazyGames SDK is not loaded');
      return;
    }
    var sdk = window.CrazyGames.SDK;
    sdk.init().then(function () {
      // 'disabled' — not on CrazyGames (another domain): every SDK call would throw.
      if (sdk.environment === 'disabled') throw new Error('The CrazyGames SDK is disabled on this domain');
      state.sdk = sdk;
      try { sdk.game.loadingStart(); } catch (e) { }
      sdk.game.addSettingsChangeListener(function (settings) {
        state.reply(0, settings && settings.muteAudio ? 4 : 5, null);
      });
      if (!sdk.user.isUserAccountAvailable) return null;
      sdk.user.addAuthListener(function (user) {
        state.user = user;
        if (user) state.reply(0, 6, null);
      });
      return sdk.user.getUser().then(function (user) { state.user = user; }).catch(function () { });
    }).then(function () {
      var info = sdk.user.systemInfo;
      state.reply(id, 1, info && info.locale ? info.locale : '');
    }).catch(function (e) {
      state.sdk = null;
      state.reply(id, 0, String(e));
    });
  },

  MazeCrazy_MuteAudio: function () {
    var sdk = window.mazeCrazy && window.mazeCrazy.sdk;
    return sdk && sdk.game.settings && sdk.game.settings.muteAudio ? 1 : 0;
  },

  MazeCrazy_AccountAvailable: function () {
    var sdk = window.mazeCrazy && window.mazeCrazy.sdk;
    return sdk && sdk.user.isUserAccountAvailable ? 1 : 0;
  },

  MazeCrazy_IsSignedIn: function () {
    return window.mazeCrazy && window.mazeCrazy.user ? 1 : 0;
  },

  MazeCrazy_SignIn: function (id) {
    var state = window.mazeCrazy;
    state.sdk.user.showAuthPrompt()
      .then(function (user) { state.user = user; state.reply(id, user ? 1 : 0, null); })
      .catch(function (e) { state.reply(id, 0, e && e.code ? e.code : String(e)); }); // userCancelled…
  },

  // The Data module works like localStorage (synchronous): the reply comes at once.
  MazeCrazy_GetData: function (id) {
    var state = window.mazeCrazy;
    try {
      var json = state.sdk.data.getItem('save');
      state.reply(id, 1, json == null ? '' : json);
    } catch (e) {
      state.reply(id, 0, e && e.code ? e.code : String(e));
    }
  },

  MazeCrazy_SetData: function (id, json) {
    var state = window.mazeCrazy;
    try {
      state.sdk.data.setItem('save', UTF8ToString(json));
      state.reply(id, 1, null);
    } catch (e) {
      state.reply(id, 0, e && e.code ? e.code : String(e));
    }
  },

  // Codes = Maze.Application.Platform.AdResult: 0 rewarded, 1 closed, 2 failed, 3 unavailable, 4 disabled.
  MazeCrazy_ShowAd: function (id, rewarded) {
    var state = window.mazeCrazy;
    var done = false;
    function end(code, text) { if (!done) { done = true; state.reply(id, code, text); } }
    state.sdk.ad.requestAd(rewarded ? 'rewarded' : 'midgame', {
      adStarted: function () { state.reply(0, 3, null); },
      adFinished: function () { end(rewarded ? 0 : 1, null); },
      adError: function (error) {
        var code = error && error.code;
        if (code === 'adsDisabledBasicLaunch') end(4, code);
        else if (code === 'unfilled' || code === 'adCooldown' || code === 'adblock') end(3, code);
        else end(2, code || String(error));
      }
    });
  },

  MazeCrazy_LoadingStop: function () {
    var sdk = window.mazeCrazy && window.mazeCrazy.sdk;
    if (sdk) sdk.game.loadingStop();
  },

  MazeCrazy_Gameplay: function (start) {
    var sdk = window.mazeCrazy && window.mazeCrazy.sdk;
    if (!sdk) return;
    if (start) sdk.game.gameplayStart();
    else sdk.game.gameplayStop();
  },

  MazeCrazy_HappyTime: function () {
    var sdk = window.mazeCrazy && window.mazeCrazy.sdk;
    if (sdk) sdk.game.happytime();
  },

  // The completed part of the game, 0–100 (newer SDK versions only).
  MazeCrazy_GameProgress: function (percent) {
    var sdk = window.mazeCrazy && window.mazeCrazy.sdk;
    if (sdk && typeof sdk.game.reportGameCompletedPercentage === 'function') sdk.game.reportGameCompletedPercentage(percent);
  }
});
