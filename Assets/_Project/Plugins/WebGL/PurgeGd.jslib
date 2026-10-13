// GameDistribution HTML5 SDK (Purge.Application.Platform.GdBridge). The page sets GD_OPTIONS and loads the SDK
// (WebPlatforms.Head): its onEvent keeps SDK_READY / SDK_ERROR in window.purgeGd and passes every event to the listener
// set here. Async calls answer through one callback: (request id, code, text or 0). Id 0 — events from the site
// (code 1: pause the game, 2: go on — the SDK's own ads; 3: the ad asked for has started).
mergeInto(LibraryManager.library, {
  PurgeGd_Init: function (callback, id) {
    var state = window.purgeGdBridge = { callback: callback, ad: null, paused: false };
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
    var gd = window.purgeGd;
    if (!gd) {
      state.reply(id, 0, 'The GameDistribution SDK is not set up on this page');
      return;
    }
    gd.listener = function (event) {
      var ad = state.ad;
      switch (event && event.name) {
        case 'SDK_GAME_PAUSE':
          if (ad) ad.start();
          else if (!state.paused) { state.paused = true; state.reply(0, 1, null); }
          break;
        case 'SDK_GAME_START':
          if (ad) ad.resume();
          else if (state.paused) { state.paused = false; state.reply(0, 2, null); }
          break;
        case 'SDK_REWARDED_WATCH_COMPLETE':
          if (ad) ad.rewarded = true;
          break;
        case 'AD_ERROR':
          // The SDK still resolves showAd (no fill: IMA 303…): kept so the result says why there was no ad.
          if (ad) ad.error = 'AD_ERROR ' + (event.message || '');
          break;
      }
    };
    // SDK_READY may have come before the game loaded: wait for the flag (the game gives up after its own timeout).
    var timer = setInterval(function () {
      if (gd.error) {
        clearInterval(timer);
        state.reply(id, 0, gd.error);
      } else if (gd.ready && window.gdsdk) {
        clearInterval(timer);
        state.preload();
        state.reply(id, 1, '');
      }
    }, 100);
    // A rewarded ad loaded ahead: showAd('rewarded') takes it, so the player's button answers at once.
    state.preload = function () {
      try {
        var sdk = window.gdsdk;
        if (sdk && typeof sdk.preloadAd === 'function') {
          var p = sdk.preloadAd('rewarded');
          if (p && typeof p.catch === 'function') p.catch(function () { });
        }
      } catch (e) { }
    };
  },

  // Codes = Purge.Application.Platform.AdResult: 0 rewarded, 1 closed, 2 failed, 3 unavailable.
  PurgeGd_ShowAd: function (id, rewarded) {
    var state = window.purgeGdBridge;
    var sdk = window.gdsdk;
    if (!sdk || typeof sdk.showAd !== 'function') {
      state.reply(id, 3, 'The GameDistribution SDK is not ready');
      return;
    }
    var done = false;
    var ad = state.ad = {
      rewarded: false,
      started: false,
      error: null,
      start: function () {
        if (ad.started) return;
        ad.started = true;
        state.reply(0, 3, null);
      },
      resume: function () { }
    };
    function end(code, text) {
      if (done) return;
      done = true;
      if (state.ad === ad) state.ad = null;
      state.reply(id, code, text);
      if (rewarded) state.preload();
    }
    function reason(e) { return e && e.message ? e.message : String(e); }
    // The reward event may come just after the promise: the result waits a moment for it. Resolved without the ad
    // ever starting (the SDK found none) — no ad: unavailable, with the SDK's reason.
    function finish() {
      setTimeout(function () {
        if (!ad.started) end(3, ad.error || 'no ad was shown');
        else end(rewarded ? (ad.rewarded ? 0 : 1) : 1, null);
      }, 250);
    }
    var promise;
    try {
      promise = rewarded ? sdk.showAd('rewarded') : sdk.showAd();
    } catch (e) {
      end(2, reason(e));
      return;
    }
    if (promise && typeof promise.then === 'function') {
      // Rejected: no ad now (no fill, an ad blocker, too soon) — no reward.
      promise.then(finish, function (e) { end(3, reason(e)); });
    } else {
      // An SDK without promises: the ad ends with SDK_GAME_START; no pause soon — no ad.
      ad.resume = finish;
      setTimeout(function () { if (!ad.started) end(3, 'no ad'); }, 5000);
    }
  }
});
