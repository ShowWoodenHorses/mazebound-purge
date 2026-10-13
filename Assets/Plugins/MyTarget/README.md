# VK Ad Network (ex. myTarget) — Unity plugin

Sources of `myTarget.unitypackage` 1.4.0 (https://github.com/myTargetSDK/mytarget-unity, LGPL-3.0, see LICENSE):
`Library/` without the iOS implementation, unchanged except one fix: `CustomParamsProxy.SetIcqIds(null)` called
`setIcqIds` with no arguments (a null `params` array) — `NoSuchMethodError` on Android broke every ad's creation, no ad
ever loaded; now null is skipped ("Maze patch" comment). Not taken: the bundled External Dependency Manager (the project
has it as a package), the demo, `MyTargetAndroidPostProcessor` (its Gradle property
`android.useFullClasspathForDexingTransform=true` is in `Assets/Plugins/Android/gradleTemplate.properties`).
Own: `MyTarget.asmdef` (Android and editor only), `Editor/MyTargetDependencies.xml` (Android SDK pinned to 5.27.4).
Used by `Maze.Application.Android` → `VkAdsProvider`.
