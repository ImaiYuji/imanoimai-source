#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// iOS 向けに書き出した Xcode プロジェクトへ、Unity が面倒を見ない設定を足す。
    ///
    ///  - 写真の追加許可の説明文(これが無いと保存時にアプリが落ちる)
    ///  - Photos.framework のリンク(ARPhotoSave.mm が使う)
    ///  - AVFoundation.framework のリンク(ARCameraPermission.mm が使う)
    ///
    /// 手で Xcode をいじると次のビルドで消えるので、ここで毎回入れ直す。
    /// </summary>
    public sealed class ARIosPostProcess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        const string k_PhotoAddUsage =
            "撮った写真を保存するために使います。写真を読み取ることはありません。";

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            var root = report.summary.outputPath;

            AddPlistEntries(root);
            LinkFrameworks(root);
        }

        static void AddPlistEntries(string root)
        {
            var path = Path.Combine(root, "Info.plist");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ARIosPostProcess] Info.plist が見つかりません: {path}");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(path);

            // 説明文が無いまま保存を試みると、iOS がアプリを強制終了させる
            plist.root.SetString("NSPhotoLibraryAddUsageDescription", k_PhotoAddUsage);

            plist.WriteToFile(path);
            Debug.Log("[ARIosPostProcess] Info.plist に写真の許可説明を追加しました。");
        }

        static void LinkFrameworks(string root)
        {
            var projectPath = PBXProject.GetPBXProjectPath(root);
            if (!File.Exists(projectPath))
            {
                Debug.LogWarning($"[ARIosPostProcess] Xcode プロジェクトが見つかりません: {projectPath}");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            var target = project.GetUnityFrameworkTargetGuid();
            project.AddFrameworkToProject(target, "Photos.framework", false);
            project.AddFrameworkToProject(target, "AVFoundation.framework", false);

            project.WriteToFile(projectPath);
            Debug.Log("[ARIosPostProcess] Photos / AVFoundation をリンクしました。");
        }
    }
}
#endif
