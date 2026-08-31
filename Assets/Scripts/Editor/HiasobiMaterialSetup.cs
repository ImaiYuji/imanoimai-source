using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// 2 体目に lilToon のマテリアルを貼る。
    ///
    /// ヒアソビの FBX は Blender で素体と衣装を合成したもので、マテリアルは
    /// 名前だけが残り、中身は Unity 既定の Standard になってしまっている。
    /// そのままだと真っ白なプラスチックのような見た目になる。
    ///
    /// どの枠に何を貼るかは推測していない。衣装の作者が配布している
    /// 公式プレハブ (DEVILs MANIA Prefab for Anomea) の FBX には
    /// externalObjects として「元のマテリアル名 → 実マテリアル」の対応表が
    /// 入っており、その中身をそのまま写している。
    /// 素体 (Anomea_Body / Anomea_Face) は 1 体目と同じ preset_1 を使う。
    /// </summary>
    static class HiasobiMaterialSetup
    {
        const string k_Devil = "Assets/てんぱすおおもり/DEVILs MANIA/Material";
        const string k_Anomea = "Assets/Anomea/Materials/Variations/preset_1";

        /// <summary>枠の名前 → 貼るマテリアル。名前は Blender の連番 (.001 など) を落として比べる。</summary>
        static readonly Dictionary<string, string> k_Map = new()
        {
            // ---- 衣装：公式プレハブの対応表をそのまま写したもの ----
            { "silver",     k_Devil + "/α/silver 1.mat" },
            { "silver2",    k_Devil + "/α/silver 2.mat" },
            { "silver3",    k_Devil + "/α/silver 1.mat" },
            { "carabiner",  k_Devil + "/α/silver 1.mat" },
            { "parts1",     k_Devil + "/α/black.mat" },
            { "jem",        k_Devil + "/α/aurora.mat" },
            { "nail",       k_Devil + "/Nail/all_black.mat" },
            { "mat1",       k_Devil + "/Bustier_Skirt/black.mat" },
            { "mat2",       k_Devil + "/Jacket/black.mat" },
            { "mat3",       k_Devil + "/Boots_Hat_Bag/black.mat" },
            { "mat4",       k_Devil + "/Inner_Socks_Armcover/Anomea/black.mat" },
            { "boots_belt", k_Devil + "/Boots_Hat_Bag/black.mat" },
            { "hane",       k_Devil + "/Boots_Hat_Bag/black.mat" },

            // ---- 衣装：日本語に付け替えられている枠 ----
            // 公式の対応表に名前が無いので、部位から同じ系統のものを充てる。
            // 上着=mat2、ブラとスカート=mat1、インナー=mat4、帽子=mat3 の系統。
            { "上着",       k_Devil + "/Jacket/black.mat" },
            { "ブラ",       k_Devil + "/Bustier_Skirt/black.mat" },
            { "スカート",   k_Devil + "/Bustier_Skirt/black.mat" },
            { "インナー",   k_Devil + "/Inner_Socks_Armcover/Anomea/black.mat" },
            { "帽子",       k_Devil + "/Boots_Hat_Bag/black.mat" },

            // ---- 素体：1 体目と同じ preset_1 ----
            { "Anomea_Body", k_Anomea + "/Body/Anomea_Body_pre1.mat" },
            { "Anomea_Face", k_Anomea + "/Face/Anomea_Face_pre1.mat" },
            { "Material",    k_Anomea + "/Hair/Anomea_Hair_pre1.mat" },

            // ---- メガネ ----
            { "Flame",     k_Devil + "/α/silver 1.mat" },
            { "Nose pad",  k_Devil + "/α/black.mat" },
            { "Lens",      k_Devil + "/α/silver 2.mat" },
        };

        /// <summary>Blender が付ける連番 (.001 / .002 …) を落とす。</summary>
        static string BaseName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            var trimmed = name.TrimEnd('.');
            var dot = trimmed.LastIndexOf('.');
            if (dot > 0 && dot == trimmed.Length - 4 && trimmed.Skip(dot + 1).All(char.IsDigit))
                return trimmed[..dot];
            return trimmed;
        }

        public static void Apply(GameObject instance)
        {
            var cache = new Dictionary<string, Material>();
            int applied = 0, missing = 0;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    // メガネの枠は Unity 組み込みの Default-Material が入っていて
                    // 元の名前が残っていない。その場合はレンダラー名で引く。
                    var raw = materials[i] == null || materials[i].name == "Default-Material"
                        ? renderer.name
                        : materials[i].name;
                    var slot = BaseName(raw);

                    if (!k_Map.TryGetValue(slot, out var path))
                    {
                        Debug.LogWarning($"[HiasobiMaterialSetup] 対応表に無い枠: '{slot}' "
                                         + $"({renderer.name}[{i}])。そのままにします。");
                        missing++;
                        continue;
                    }

                    if (!cache.TryGetValue(path, out var material))
                    {
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        cache[path] = material;
                        if (material == null)
                            Debug.LogError($"[HiasobiMaterialSetup] {path} が見つかりません。");
                    }

                    if (material == null)
                    {
                        missing++;
                        continue;
                    }

                    materials[i] = material;
                    changed = true;
                    applied++;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            Debug.Log($"[HiasobiMaterialSetup] lilToon のマテリアルを {applied} 枠に貼りました"
                      + (missing > 0 ? $"（未対応 {missing} 枠）" : "。"));
        }
    }
}
