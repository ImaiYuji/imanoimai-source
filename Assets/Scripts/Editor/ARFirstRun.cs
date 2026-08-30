using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// プロジェクトを初めて開いたときに、セットアップを走らせるか一度だけ尋ねる。
    ///
    /// 勝手に走らせるとシーンを上書きされて驚かれるので、必ず確認を挟む。
    /// </summary>
    [InitializeOnLoad]
    static class ARFirstRun
    {
        const string k_Key = "ARCharacterApp.FirstRunCompleted";

        static ARFirstRun()
        {
            // コンパイル直後に UI を出すと不安定なので 1 フレーム遅らせる
            EditorApplication.delayCall += Prompt;
        }

        static void Prompt()
        {
            // CI / バッチ実行では対話しない。ダイアログは既定の選択肢が返るだけで無意味なうえ、
            // -executeMethod で明示的に呼ばれる処理と二重に走ってしまう。
            if (Application.isBatchMode)
                return;

            var key = $"{k_Key}.{Application.dataPath.GetHashCode()}";

            if (EditorPrefs.GetBool(key, false))
                return;

            EditorPrefs.SetBool(key, true);

            var choice = EditorUtility.DisplayDialogComplex(
                "AR Character App のセットアップ",
                "AR アプリとして動く状態まで自動で設定します。\n\n" +
                "・Android / iOS のプレイヤー設定\n" +
                "・XR Plug-in Management (ARCore) の有効化\n" +
                "・AR シーン一式の生成 (Assets/Scenes/AR.unity)\n\n" +
                "実行しますか?",
                "すべて実行",
                "あとで",
                "設定だけ実行");

            switch (choice)
            {
                case 0:
                    ARProjectSetup.ConfigureProject();
                    ARSceneBuilder.BuildScene();
                    break;
                case 2:
                    ARProjectSetup.ConfigureProject();
                    break;
            }
        }

        [MenuItem("AR Character App/セットアップの確認をリセット", priority = 100)]
        static void ResetFirstRun()
        {
            EditorPrefs.DeleteKey($"{k_Key}.{Application.dataPath.GetHashCode()}");
            Debug.Log("[ARFirstRun] 次回プロジェクトを開いたときに再度確認します。");
        }
    }
}
