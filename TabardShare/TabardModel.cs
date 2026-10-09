using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TabardShare
{
    /// <summary>
    ///     The tabard mesh (from the embedded "tabardshare" asset bundle), the material it's drawn with,
    ///     and attaching it to a character.
    ///
    ///     Attaching binds the tabard's SkinnedMeshRenderer to the body's bones BY NAME. Vanilla armour
    ///     (VisEquipment.AttachArmor) instead hands over the body's whole bones array, which only works if
    ///     the mesh was exported with the exact same bone order. Matching by name removes that dependency
    ///     (proven in the editor test, 2026-10-08).
    /// </summary>
    internal static class TabardModel
    {
        private const string BundleName = "tabardshare";
        private const string ModelAssetName = "tabard";
        private const string MaterialSourcePrefab = "CapeLinen";

        private static AssetBundle bundle;
        private static GameObject model;
        private static Material template;

        private static readonly MethodInfo UpdateLodgroup = AccessTools.Method(typeof(VisEquipment), "UpdateLodgroup");
        private static readonly FieldInfo ModelIndexField = AccessTools.Field(typeof(VisEquipment), "m_modelIndex");

        /// <summary>Name of the shape key (Unity: blend shape) that refits the tabard for the female body.</summary>
        public const string FemaleShapeKey = "Female";

        /// <summary>VisEquipment.m_models: 0 = male body (body_38), 1 = female body (bodyfem_1).</summary>
        private const int FemaleModelIndex = 1;

        /// <summary>Loads the embedded bundle and finds the model. Call once, from Awake.</summary>
        public static bool LoadBundle()
        {
            // Not Jotunn's AssetUtils.LoadAssetBundleFromResources: it loads from a stream it then closes, and
            // Unity reads bundles from their stream lazily - so the first LoadAsset fails with "ManagedStream
            // object must be readable" plus a cascade of "Mismatched serialization" errors. Loading the
            // bundle from a byte array keeps the data alive for the bundle's lifetime.
            byte[] data = ReadResource(BundleName);
            if (data == null)
            {
                Jotunn.Logger.LogError($"[TabardShare] The '{BundleName}' asset bundle isn't embedded in the DLL.");
                return false;
            }
            bundle = AssetBundle.LoadFromMemory(data);
            if (bundle == null)
            {
                Jotunn.Logger.LogError($"[TabardShare] The '{BundleName}' asset bundle couldn't be loaded (built with a different Unity version?).");
                return false;
            }

            model = bundle.LoadAsset<GameObject>(ModelAssetName);
            if (model == null || model.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
            {
                Jotunn.Logger.LogError($"[TabardShare] '{ModelAssetName}' in the asset bundle has no skinned mesh. Assets: {string.Join(", ", bundle.GetAllAssetNames())}");
                return false;
            }

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Jotunn.Logger.LogInfo($"[TabardShare] Tabard model loaded: mesh '{smr.sharedMesh.name}', {smr.sharedMesh.vertexCount} vertices, {smr.bones.Length} bones.");
            return true;
        }

        /// <summary>
        ///     Builds the base material from a vanilla cape's: same Custom/Creature shader, and _Cull 0 so the
        ///     single-sided tabard draws from both sides, exactly like vanilla capes. The cape's own style
        ///     atlas and normal map are switched off - they're laid out for the cape's UVs, not ours.
        /// </summary>
        public static void SetupMaterialTemplate()
        {
            var cape = PrefabManager.Instance.GetPrefab(MaterialSourcePrefab);
            var capeRenderer = cape != null ? cape.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault() : null;
            Material source = capeRenderer != null ? capeRenderer.sharedMaterial : null;
            if (source == null)
            {
                Jotunn.Logger.LogWarning($"[TabardShare] Couldn't find {MaterialSourcePrefab}'s material - falling back to Custom/Creature.");
                var shader = Shader.Find("Custom/Creature") ?? Shader.Find("Standard");
                source = new Material(shader);
            }

            template = new Material(source) { name = "TabardShare_Template" };
            SetFloatIfPresent(template, "_UseStyles", 0f);
            template.DisableKeyword("_USESTYLES_ON");
            if (template.HasProperty("_StyleTex"))
                template.SetTexture("_StyleTex", null);
            if (template.HasProperty("_BumpMap"))
                template.SetTexture("_BumpMap", null);
            template.DisableKeyword("_NORMALMAP");
            SetFloatIfPresent(template, "_Cull", 0f);
            if (template.HasProperty("_Color"))
                template.SetColor("_Color", Color.white);

            Jotunn.Logger.LogInfo($"[TabardShare] Tabard material based on '{source.name}' (shader '{source.shader.name}').");
        }

        /// <summary>A tabard material for one style. <paramref name="bumpMap"/> and <paramref name="metalMask"/> are optional (null).</summary>
        public static Material CreateMaterial(Texture2D mainTex, Texture2D bumpMap, Texture2D metalMask, string name)
        {
            var mat = new Material(template) { name = name };
            mat.SetTexture("_MainTex", mainTex);
            if (bumpMap != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bumpMap);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (metalMask != null && mat.HasProperty("_MetallicGlossMap"))
            {
                // Custom/Creature: _Metallic scales the mask (the cape's is 0, i.e. no metal at all).
                mat.SetTexture("_MetallicGlossMap", metalMask);
                SetFloatIfPresent(mat, "_Metallic", 1f);
            }
            return mat;
        }

        /// <summary>
        ///     Instantiates the tabard on a character and binds it to the body's bones by name.
        ///     Returns the new GameObject (child of the character's Visual), or null if it couldn't be bound.
        /// </summary>
        public static GameObject Attach(VisEquipment vis, Material material)
        {
            var body = vis != null ? vis.m_bodyModel : null;
            if (body == null || model == null)
                return null;

            var bodyBones = new Dictionary<string, Transform>();
            foreach (var bone in body.bones)
                if (bone != null)
                    bodyBones[bone.name] = bone;

            Transform parent = body.transform.parent;
            var instance = Object.Instantiate(model, body.transform.position, parent.rotation, parent);
            instance.name = "TabardShare_Tabard";

            foreach (var animator in instance.GetComponentsInChildren<Animator>(true))
                Object.Destroy(animator);

            var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var newBones = new Transform[smr.bones.Length];
            var missing = new List<string>();
            for (int i = 0; i < smr.bones.Length; i++)
            {
                string boneName = smr.bones[i] != null ? smr.bones[i].name : "(null)";
                if (!bodyBones.TryGetValue(boneName, out newBones[i]))
                    missing.Add(boneName);
            }
            if (missing.Count > 0)
            {
                Jotunn.Logger.LogError($"[TabardShare] The tabard uses bones this character doesn't have: {string.Join(", ", missing)}");
                Object.Destroy(instance);
                return null;
            }

            // The copy of the skeleton that came with the model isn't needed once the mesh uses the body's bones.
            Transform rootBoneCopy = smr.rootBone;
            smr.bones = newBones;
            smr.rootBone = body.rootBone;
            smr.sharedMaterial = material;

            // Bounds are relative to the root bone (Hips) - borrow the body's, a little larger, so the tabard
            // isn't culled while still on screen.
            var bounds = body.localBounds;
            bounds.Expand(0.3f);
            smr.localBounds = bounds;

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = body.gameObject.layer;

            // The mesh object may sit under the model's own armature (it was parented to it in Blender).
            // Move it up to the instance root first, so removing the armature copy doesn't take it along.
            if (smr.transform.parent != instance.transform)
                smr.transform.SetParent(instance.transform, true);

            if (rootBoneCopy != null)
            {
                // Remove the model's own armature (the top-level child holding the bone copies).
                Transform top = rootBoneCopy;
                while (top.parent != null && top.parent != instance.transform)
                    top = top.parent;
                if (top.parent == instance.transform)
                    Object.Destroy(top.gameObject);
            }

            ApplyBodyShape(instance, GetModelIndex(vis));
            instance.SetActive(true);
            RefreshLods(vis);
            return instance;
        }

        /// <summary>Which body the character uses (see FemaleModelIndex). Remote players' VisEquipment keeps this in sync from their ZDO.</summary>
        public static int GetModelIndex(VisEquipment vis)
        {
            if (vis == null || ModelIndexField == null)
                return 0;
            return (int)ModelIndexField.GetValue(vis);
        }

        /// <summary>Switches the "Female" shape key fully on for the female body and off otherwise.</summary>
        public static void ApplyBodyShape(GameObject instance, int modelIndex)
        {
            if (instance == null)
                return;
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null)
                    continue;
                int shape = mesh.GetBlendShapeIndex(FemaleShapeKey);
                if (shape >= 0)
                    smr.SetBlendShapeWeight(shape, modelIndex == FemaleModelIndex ? 100f : 0f);
            }
        }

        public static void RefreshLods(VisEquipment vis)
        {
            if (vis == null || UpdateLodgroup == null)
                return;
            try
            {
                UpdateLodgroup.Invoke(vis, null);
            }
            catch
            {
                // Best effort - without it the tabard just doesn't take part in LOD switching.
            }
        }

        /// <summary>An embedded resource's bytes (matched by the end of its name), or null.</summary>
        internal static byte[] ReadResource(string fileName)
        {
            var asm = typeof(TabardModel).Assembly;
            string resource = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, System.StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                return null;
            using (var stream = asm.GetManifestResourceStream(resource))
            using (var memory = new System.IO.MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static void SetFloatIfPresent(Material mat, string property, float value)
        {
            if (mat.HasProperty(property))
                mat.SetFloat(property, value);
        }
    }
}
