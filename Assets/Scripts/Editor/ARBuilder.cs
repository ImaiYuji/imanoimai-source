using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>APK / AAB を吐くためのビルド入口。CI からも叩けるように static メソッドを公開してある。</summary>
    public static class ARBuilder
    {
        const string k_OutputDir = "Build";

        [MenuItem("AR Character App/3. Android APK をビルド", priority = 30)]
        public static void BuildAndroidApk()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            Build($"{k_OutputDir}/ARCharacter.apk");
        }

        [MenuItem("AR Character App/4. Android AAB をビルド (ストア提出用)", priority = 31)]
        public static void BuildAndroidAab()
        {
            EditorUserBuildSettings.buildAppBundle = true;
            Build($"{k_OutputDir}/ARCharacter.aab");
        }

        [MenuItem("AR Character App/5. iOS プロジェクトを書き出し", priority = 32)]
        public static void BuildIosProject()
        {
            // iOS は APK のように 1 ファイルにはならない。
            // ここで作るのは Xcode プロジェクトで、そこから先は Xcode の担当。
            var scenes = EnabledScenes();
            if (scenes.Length == 0)
                return;

            EnsureSceneOpen(scenes[0]);

            var output = $"{k_OutputDir}/iOS";
            if (!Directory.Exists(output))
                Directory.CreateDirectory(output);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.iOS,
                targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.None,
            });

            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"[ARBuilder] iOS プロジェクトを書き出しました: {output} "
                    + $"({report.summary.totalTime.TotalSeconds:F0} 秒)");
            else
                Debug.LogError($"[ARBuilder] iOS の書き出しに失敗: {report.summary.result} "
                    + $"(エラー {report.summary.totalErrors} 件)");
        }

        /// <summary>CI 用: Unity.exe -executeMethod ARCharacterApp.EditorTools.ARBuilder.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--output");
            var output = index >= 0 && index + 1 < args.Length
                ? args[index + 1]
                : $"{k_OutputDir}/ARCharacter.apk";

            EditorUserBuildSettings.buildAppBundle = output.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);

            var report = Build(output);
            if (report == null || report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        /// <summary>
        /// 配布用の署名を環境変数から読んで適用する。
        ///
        /// 鍵のパスワードはプロジェクトに置かない。
        /// ProjectSettings に書くと git に入ってしまい、
        /// リポジトリを見られた時点で誰でも同じ署名の APK を作れてしまう。
        ///
        ///   IMANOIMAI_KEYSTORE       .keystore のパス
        ///   IMANOIMAI_KEYSTORE_PASS  ストアのパスワード
        ///   IMANOIMAI_KEY_ALIAS      鍵の別名
        ///   IMANOIMAI_KEY_PASS       鍵のパスワード
        ///
        /// 揃っていなければデバッグ鍵のまま。手元で試すぶんには困らないが、
        /// 配る APK は必ずこちらで署名すること。
        /// デバッグ鍵は環境ごとに違うので、配ったあとに鍵が変わると
        /// 利用者が上書き更新できなくなる。
        /// </summary>
        static void ApplySigning()
        {
            var keystore = Environment.GetEnvironmentVariable("IMANOIMAI_KEYSTORE");
            var keystorePass = Environment.GetEnvironmentVariable("IMANOIMAI_KEYSTORE_PASS");
            var alias = Environment.GetEnvironmentVariable("IMANOIMAI_KEY_ALIAS");
            var aliasPass = Environment.GetEnvironmentVariable("IMANOIMAI_KEY_PASS");

            var ready = !string.IsNullOrEmpty(keystore)
                        && !string.IsNullOrEmpty(keystorePass)
                        && !string.IsNullOrEmpty(alias)
                        && !string.IsNullOrEmpty(aliasPass);

            if (!ready)
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[ARBuilder] 配布用の鍵が指定されていません。デバッグ鍵で署名します。"
                    + " 配布する APK には IMANOIMAI_KEYSTORE 等を設定してください。");
                return;
            }

            if (!File.Exists(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogError($"[ARBuilder] 鍵が見つかりません: {keystore}");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPass;

            Debug.Log($"[ARBuilder] 配布用の鍵で署名します: {Path.GetFileName(keystore)} (別名 {alias})");
        }

        /// <summary>
        /// 開いているシーンが無ければ開く。バッチモード対策(呼び出し元のコメント参照)。
        /// </summary>
        static void EnsureSceneOpen(string scenePath)
        {
            if (!string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path))
                return;

            if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
                return;

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[ARBuilder] バッチモード用にシーンを開きました: {scenePath}");
        }

        static string[] EnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
                Debug.LogError("[ARBuilder] Build Settings にシーンがありません。" +
                               "先に「2. AR シーンを生成」を実行してください。");

            return scenes;
        }

        static BuildReport Build(string outputPath)
        {
            var scenes = EnabledScenes();
            if (scenes.Length == 0)
                return null;

            ApplySigning();

            // バッチモードではシーンが 1 つも開かれていない。
            // その状態だと lilToon のビルド前処理が落ちる。
            //
            //   lilToonSetting.ApplyShaderSettingOptimized は
            //     1. 全シェーダー機能をオフにする
            //     2. シーンを走査して、実際に使う機能だけを戻す
            //   という順で動くが、2 の最後に「元のシーンへ戻す」で
            //   EditorSceneManager.OpenScene("") を呼び、
            //   ArgumentException: Scene file not found: '' で死ぬ。
            //
            // 例外で 2 が中断されるので全機能オフのまま焼かれ、
            // lilToon のサブシェーダーが全部消える
            // (WARNING: Shader Unsupported: 'lilToon' - All subshaders removed)。
            // するとマテリアルはフォールバックで描かれ、アルファが効かなくなる。
            //
            // シーンを開いておけば GetActiveScene().path が埋まり、この経路を通らない。
            EnsureSceneOpen(scenes[0]);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[ARBuilder] ビルド成功: {outputPath} " +
                          $"({summary.totalSize / (1024f * 1024f):F1} MB / {summary.totalTime.TotalSeconds:F0} 秒)");
            }
            else
            {
                Debug.LogError($"[ARBuilder] ビルド失敗: {summary.result} " +
                               $"(エラー {summary.totalErrors} 件)");
            }

            return report;
        }
    }
}
