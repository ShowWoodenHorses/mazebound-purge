# MyTracker — Unity plugin

Sources of `myTracker.unitypackage` 3.0.4 (https://github.com/myTrackerSDK/mytracker-unity, LGPL-3.0, see LICENSE):
`Library/` without the iOS implementation, unchanged. Not taken: the bundled External Dependency Manager, the demo,
`Plugins/Android/manifest_mixin.aar` (a legacy install-referrer receiver; the SDK's own manifest has what it needs).
Own: `MyTracker.asmdef` (Android and editor only), `Editor/MyTrackerDependencies.xml` (Android SDK 3.3.2).
Used by `Maze.Application.Android` → `MyTrackerSink`.
