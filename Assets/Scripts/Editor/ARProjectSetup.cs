using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// AR アプリとして成立させるためのプロジェクト設定を一括で当てるエディタ拡張。
    ///
    /// 手作業でやると必ずどこかを忘れる(min SDK、グラフィックス API、ARCore ローダー等)ので、
    /// コードに落として再現可能にしてある。
    /// </summary>
    public static class ARProjectSetup
    {
        const string k_ProductName = "いまのいまい";
        const string k_CompanyName = "今井カンパニー";

        /// <summary>
        /// 配布用のパッケージ名。
        /// いったん配ったあとに変えると、利用者側では別アプリ扱いになり
        /// 上書き更新ができなくなる。配る前に決め切ること。
        /// </summary>
        const string k_AndroidPackage = "com.imaiyuji.imanoimai";

        /// <summary>画面や配布ページに出るバージョン。</summary>
        const string k_VersionName = "1.0.0";

        /// <summary>
        /// 更新のたびに増やす番号。
        /// これが増えていないと、端末は「同じか古い版」とみなして更新を拒む。
        /// </summary>
        const int k_VersionCode = 1;

        const string k_CameraUsage =
            "現実の空間にキャラクターを表示するために、カメラを使用します。";

        const string k_IconPath = "Assets/Art/AppIcon.png";

        [MenuItem("AR Character App/1. プロジェクト設定を適用", priority = 10)]
        public static void ConfigureProject()
        {
            ConfigureGeneral();
            ConfigureAndroid();
            ConfigureIOS();
            ConfigureInputHandling();
            ConfigureXRLoaders();
            ConfigureAppIcon();

            AssetDatabase.SaveAssets();
            Debug.Log("[ARProjectSetup] プロジェクト設定を適用しました。");
        }

        /// <summary>
        /// アプリアイコンを設定する。
        /// 必要な解像度は Unity 側が持っているので、同じ画像を全サイズに渡して縮小させる。
        /// </summary>
        static void ConfigureAppIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(k_IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"[ARProjectSetup] {k_IconPath} が無いため、アイコンは設定しません。");
                return;
            }

            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                var sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
                if (sizes == null || sizes.Length == 0)
                    continue;

                var icons = new Texture2D[sizes.Length];
                for (var i = 0; i < icons.Length; i++)
                    icons[i] = icon;

                PlayerSettings.SetIcons(target, icons, IconKind.Application);
            }

            // Android 8 以降は Adaptive Icon が使われる。
            // 前景に絵柄、背景に絵柄の下地色を敷いて、丸や角丸に切り抜かれても破綻しないようにする。
            SetAndroidAdaptiveIcon(icon);

            Debug.Log($"[ARProjectSetup] アプリアイコンに {k_IconPath} を設定しました。");
        }

        static void ConfigureGeneral()
        {
            PlayerSettings.companyName = k_CompanyName;
            PlayerSettings.productName = k_ProductName;
            PlayerSettings.bundleVersion = k_VersionName;

            // AR は基本的に縦持ち。回転で酔うのを避けるため縦固定にしておく。
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // AR 中に画面が消えると体験が途切れる
            PlayerSettings.MTRendering = true;
        }

        /// <summary>既存の定義を消さずにスクリプト定義シンボルを足す。</summary>
        static void AddScriptingDefine(NamedBuildTarget target, string symbol)
        {
            var current = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = current
                .Split(';', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToList();

            if (defines.Contains(symbol))
                return;

            defines.Add(symbol);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log($"[ARProjectSetup] スクリプト定義を追加: {symbol}");
        }

        static void ConfigureAndroid()
        {
            var android = NamedBuildTarget.Android;

            PlayerSettings.SetApplicationIdentifier(android, k_AndroidPackage);

            // ARCore は API Level 24 以上が必須
            PlayerSettings.Android.bundleVersionCode = k_VersionCode;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Play ストア配信の必須要件(64bit)
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // ARCore は Vulkan だと端末によって背景描画がこけることがある。
            // OpenGLES3 に固定して事故を減らす。
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            // lilToon はビルド前処理でシェーダーファイルそのものを書き換え、
            // 「シーンで使っていない機能を全部落とす」最適化をする。
            // その処理はシーンを開き直す前提になっていて、バッチモードだと
            //   ArgumentException: Scene file not found: ''
            // で中断する。全機能をオフにした直後に落ちるため、
            // シェーダーはサブシェーダーが全滅した状態のまま焼かれる。
            //   WARNING: Shader Unsupported: 'lilToon' - All subshaders removed
            // こうなるとマテリアルは Fallback "Unlit/Texture" で描かれ、
            // アルファが無視されて Overlay が顔の上に不透明な板として出る。
            //
            // ビルド前にシーンを開く対策も入れてあるが(ARBuilder)、
            // 書き換え自体を止めるほうが確実なので、lilToon 自身が用意している
            // 定義でビルド時のアセット改変を無効にする。
            AddScriptingDefine(android, "LILTOON_DISABLE_ASSET_MODIFICATION");

            // 非対応端末にもインストールさせ、フォールバック表示に流す。
            // Required にするとストアで非対応端末に表示されなくなる。
            SetARCoreRequirement(optional: true);
        }

        static void ConfigureIOS()
        {
            // Windows では iOS ビルドはできないが、設定自体は保存しておける。
            // Mac に持っていけばそのままビルドできる状態にしておく。
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, k_AndroidPackage);

            // ビルド番号。Android の versionCode と同じ意味で、更新の判定に使われる。
            // 既定のままだと 0 になり、AltStore 等が新旧を判断できない。
            PlayerSettings.iOS.buildNumber = k_VersionCode.ToString();

            // 署名は Xcode に任せる。
            // 手動にしておくとプロビジョニングプロファイルの指定が要り、
            // 無料の Apple ID では用意できない。自動なら Xcode が面倒を見てくれる。
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;

            // Android と同じ理由で、lilToon のビルド時アセット改変を止める。
            // これをしないとサブシェーダーが全滅し、Fallback で描かれてアルファが効かなくなる。
            AddScriptingDefine(NamedBuildTarget.iOS, "LILTOON_DISABLE_ASSET_MODIFICATION");

            // ARKit のネイティブライブラリをビルドに含めるための定義。
            //
            // 本来は ARKit パッケージが自分で付けるが、バッチモードでは付かない。
            //   - ARKitBuildProcessor.OnPreprocessBuild は
            //     #if UNITY_XR_ARKIT_LOADER_ENABLED の中でしか本処理を呼ばない
            //   - その定義を付けるのは、バッチモードではその本処理の中だけ
            // という循環になっていて、定義が無いから処理が走らず、
            // 処理が走らないから定義が付かない。エディタの GUI では
            // 静的コンストラクタが先に付けるので表面化しない。
            //
            // 付いていないと loaderEnabled が false のままになり、
            // libUnityARKit.a が丸ごと除外されて
            // "Undefined symbols: _UnityARKit_*" でリンクに失敗する。
            AddScriptingDefine(NamedBuildTarget.iOS, "UNITY_XR_ARKIT_LOADER_ENABLED");
            PlayerSettings.iOS.cameraUsageDescription = k_CameraUsage;
            // ARKit のパッケージが 15.0 を要求するので、実態に合わせる。
            // 12.0 のままだと Xcode 側と食い違って警告が出る。
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.SetArchitecture(NamedBuildTarget.iOS, 1); // ARM64
        }

        /// <summary>
        /// Input System パッケージを入れつつ、旧 Input API も使えるように "Both" にする。
        /// AR Foundation の TrackedPoseDriver は新 Input System 側、
        /// 本アプリのタッチ操作は旧 API 側を使っているため。
        /// </summary>
        static void ConfigureInputHandling()
        {
            const string path = "ProjectSettings/ProjectSettings.asset";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[ARProjectSetup] ProjectSettings.asset を読めませんでした。");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null)
            {
                Debug.LogWarning("[ARProjectSetup] activeInputHandler が見つかりませんでした。");
                return;
            }

            const int both = 2;
            if (prop.intValue != both)
            {
                prop.intValue = both;
                so.ApplyModifiedProperties();
                Debug.Log("[ARProjectSetup] Active Input Handling を Both に変更しました(要 Editor 再起動)。");
            }
        }

        /// <summary>XR Plug-in Management に ARCore ローダーを登録する。</summary>
        /// <summary>指定のプラットフォームに XR のローダーを登録する。</summary>
        static void AssignLoader(XRGeneralSettingsPerBuildTarget perBuildTarget,
                                 BuildTargetGroup group, string loader, string label)
        {
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var settings = perBuildTarget.SettingsForBuildTarget(group);

            if (settings == null || settings.Manager == null)
            {
                Debug.LogWarning($"[ARProjectSetup] {group} 用の XR 設定を作成できませんでした。");
                return;
            }

            if (!XRPackageMetadataStore.AssignLoader(settings.Manager, loader, group))
                Debug.LogWarning($"[ARProjectSetup] {loader} の登録に失敗しました。");
            else
                Debug.Log($"[ARProjectSetup] {label} ローダーを有効化しました。({group})");

            EditorUtility.SetDirty(settings);
        }

        static void ConfigureXRLoaders()
        {
            var perBuildTarget = GetOrCreatePerBuildTargetSettings();
            if (perBuildTarget == null)
                return;

            AssignLoader(perBuildTarget, BuildTargetGroup.Android,
                "UnityEngine.XR.ARCore.ARCoreLoader", "ARCore");

            // iOS 側。ARKit のパッケージが入っていないと登録に失敗するだけで、
            // Android のビルドには影響しない。
            AssignLoader(perBuildTarget, BuildTargetGroup.iOS,
                "UnityEngine.XR.ARKit.ARKitLoader", "ARKit");
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTargetSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget existing) && existing != null)
            {
                return existing;
            }

            const string dir = "Assets/XR";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var created = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(created, $"{dir}/XRGeneralSettings.asset");
            AssetDatabase.SaveAssets();

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, created, true);
            return created;
        }

        /// <summary>
        /// ARCoreSettings.requirement を設定する。
        /// 型がエディタ専用アセンブリにあり asmdef 参照名がバージョンで揺れるため、リフレクションで触る。
        /// </summary>
        static void SetARCoreRequirement(bool optional)
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(SafeGetTypes)
                    .FirstOrDefault(t => t.FullName == "UnityEditor.XR.ARCore.ARCoreSettings");

                if (type == null)
                    return;

                // ARCoreSettings.currentSettings (static property) を取得
                var currentProp = type.GetProperty("currentSettings",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                var settings = currentProp?.GetValue(null) as ScriptableObject;
                if (settings == null)
                    return;

                var so = new SerializedObject(settings);
                var req = so.FindProperty("m_Requirement");
                if (req != null)
                {
                    // 0 = Optional, 1 = Required
                    req.intValue = optional ? 0 : 1;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(settings);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ARProjectSetup] ARCore Requirement を設定できませんでした: {e.Message}");
            }
        }

        /// <summary>
        /// Android の Adaptive Icon(前景 / 背景の 2 枚組)を設定する。
        /// 対応していないバージョンでも落とさないよう、種類は Unity 側に問い合わせる。
        /// </summary>
        static void SetAndroidAdaptiveIcon(Texture2D foreground)
        {
            try
            {
                // Adaptive 系は PlatformIconKind 側の API を使う(IconKind とは別系統)
                var kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
                var adaptive = kinds.FirstOrDefault(k => k.ToString().Contains("Adaptive"));

                if (adaptive == null)
                    return;

                // レイヤー 0 が背景、1 が前景。背景は絵柄の地色に近い淡い色で埋める。
                var background = CreateSolidTexture(new Color(0.96f, 0.93f, 0.96f, 1f));
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, adaptive);

                foreach (var slot in icons)
                {
                    var layers = new Texture2D[Mathf.Max(slot.maxLayerCount, 1)];
                    for (var i = 0; i < layers.Length; i++)
                        layers[i] = i == 0 ? background : foreground;

                    slot.SetTextures(layers);
                }

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, adaptive, icons);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ARProjectSetup] Adaptive Icon を設定できませんでした: {e.Message}");
            }
        }

        static Texture2D CreateSolidTexture(Color color)
        {
            const string path = "Assets/Art/AppIconBackground.png";

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            const int size = 512;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            var packed = (Color32)color;

            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = packed;

            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Type[] SafeGetTypes(System.Reflection.Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }
    }
}
