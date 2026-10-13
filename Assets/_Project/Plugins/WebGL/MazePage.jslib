// The page around the game (Maze.Application.Platform.WebPage).
mergeInto(LibraryManager.library, {
  // Calls back with 1 when the tab becomes visible and 0 when it is hidden.
  MazePage_ListenVisibility: function (callback) {
    document.addEventListener('visibilitychange', function () {
      {{{ makeDynCall('vi', 'callback') }}}(document.hidden ? 0 : 1);
    });
  },

  // Edges the game must keep clear of (system bars, notches), in the game's screen pixels: the larger of the page's
  // CSS safe-area insets and the ones a site reports (window.mazeSetSiteInsets — VK: VKWebAppUpdateConfig insets,
  // CSS pixels). Calls back (left, top, right, bottom) at start and on every change.
  MazePage_ListenInsets: function (callback) {
    var probe = document.createElement('div');
    probe.style.cssText = 'position:fixed;left:0;top:0;width:0;height:0;visibility:hidden;pointer-events:none;' +
      'padding:env(safe-area-inset-top) env(safe-area-inset-right) env(safe-area-inset-bottom) env(safe-area-inset-left);';
    document.body.appendChild(probe);
    var site = { left: 0, top: 0, right: 0, bottom: 0 };
    var last = '';
    function report() {
      var style = getComputedStyle(probe);
      var canvas = Module['canvas'];
      var scale = canvas && canvas.clientWidth > 0 ? canvas.width / canvas.clientWidth : (window.devicePixelRatio || 1);
      var edge = function (css, siteValue) { return Math.round(Math.max(parseFloat(css) || 0, siteValue || 0) * scale); };
      var left = edge(style.paddingLeft, site.left), top = edge(style.paddingTop, site.top);
      var right = edge(style.paddingRight, site.right), bottom = edge(style.paddingBottom, site.bottom);
      var key = left + ',' + top + ',' + right + ',' + bottom;
      if (key === last) return;
      last = key;
      {{{ makeDynCall('viiii', 'callback') }}}(left, top, right, bottom);
    }
    window.mazeSetSiteInsets = function (insets) {
      site = {
        left: (insets && insets.left) || 0, top: (insets && insets.top) || 0,
        right: (insets && insets.right) || 0, bottom: (insets && insets.bottom) || 0
      };
      report();
    };
    window.addEventListener('resize', function () { setTimeout(report, 0); });
    window.addEventListener('orientationchange', function () { setTimeout(report, 300); });
    report();
  }
});
