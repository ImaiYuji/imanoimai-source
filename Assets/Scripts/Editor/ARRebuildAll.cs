using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// プロジェクト設定からシーン・キャラクター・ポーズまでを一気に作り直す。
    ///
    /// 個々の手順は依存関係があり、順番を間違えると
    /// 「プレハブを作り直したせいでポーズの割り当てが消える」といった事故が起きる。
    /// 正しい順序をここに固定しておく。
    /// </summary>
    public static class ARRebuildAll
    {
        [MenuItem("AR Character App/0. すべて再構築", priority = 1)]
        public static void RebuildAll()
        {
            Debug.Log("[ARRebuildAll] 1/5 プロジェクト設定(アイコン含む)");
            ARProjectSetup.ConfigureProject();

            Debug.Log("[ARRebuildAll] 2/5 AR シーン生成");
            ARSceneBuilder.BuildScene();

            // 持ち込んだマテリアルは参照切れのテクスチャを抱えているので、
            // プレハブを作る前に繋ぎ直しておく(服が貼られなかった原因)。
            Debug.Log("[ARRebuildAll] 3/5 マテリアルの修復");
            MaterialTextureRepair.Repair();

            // Overlay は不透明シェーダーのままアルファ付きテクスチャを使っており、
            // 抜けるはずの部分が板のように塗りつぶされていた。
            OverlayTransparencyFix.Fix();

            // Anomea の FBX からプレハブを作り、ポーズ用の Animator Controller も生成する。
            // マテリアルは作者が lilToon で作り込んだものをそのまま使うので変換は挟まない。
            Debug.Log("[ARRebuildAll] 4/5 キャラクター生成(Anomea + ポーズ)");
            AnomeaCharacterSetup.Setup();


            Debug.Log("[ARRebuildAll] 5/5 シーン検証");
            ARSceneValidator.Validate();

            Debug.Log("[ARRebuildAll] 完了");
        }
    }
}
