using System;
using System.Collections.Generic;
using Murim.Core.World;
using UnityEngine;
using UnityEngine.U2D;

namespace Murim.View
{
    /// <summary>
    /// Creates and manages static 2D prop, bank-lip, and ground-shadow sprite renderers (§3.3.4).
    /// </summary>
    public sealed class PropView
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly GameObject _floraRoot;
        private readonly Material _spriteMaterial;
        private readonly Material _floraMaterial;
        private readonly Sprite[] _variantSprites;
        private readonly Sprite _shadowSprite;
        private readonly SpriteRenderer[] _propRenderers;
        private readonly SpriteRenderer[] _shadowByPropIndex;
        private readonly List<SpriteRenderer> _shadowRenderers;

        private PropView(
            int propCount,
            GameObject floraRoot,
            Material spriteMaterial,
            Material floraMaterial,
            SpriteAtlas atlas)
        {
            _floraRoot = floraRoot;
            _spriteMaterial = spriteMaterial;
            _floraMaterial = floraMaterial;
            _variantSprites = LoadVariantSprites(atlas);
            _shadowSprite = atlas != null ? atlas.GetSprite("shadow_soft") : null;
            _propRenderers = new SpriteRenderer[propCount];
            _shadowByPropIndex = new SpriteRenderer[propCount];
            _shadowRenderers = new List<SpriteRenderer>(propCount / 4);
        }

        public GameObject FloraRoot => _floraRoot;
        public Material SpriteMaterial => _spriteMaterial;
        public Material FloraMaterial => _floraMaterial;
        public Sprite ShadowSprite => _shadowSprite;
        public IReadOnlyList<SpriteRenderer> PropRenderers => _propRenderers;
        public IReadOnlyList<SpriteRenderer> ShadowRenderers => _shadowRenderers;

        public Sprite GetVariantSprite(byte variant)
        {
            return _variantSprites[variant];
        }

        public SpriteRenderer GetShadowForProp(int propIndex)
        {
            return _shadowByPropIndex[propIndex];
        }

        public static PropView Build(
            ValleyWorld world,
            SpriteAtlas atlas,
            Material worldSpriteMaterial,
            Material worldFloraMaterial,
            Transform parent)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var floraRoot = new GameObject("Flora");
            floraRoot.layer = DrawOrder.WorldLayer;
            if (parent != null)
            {
                floraRoot.transform.SetParent(parent, false);
            }

            Material floraInstance = CreateFloraMaterialInstance(worldFloraMaterial);
            var view = new PropView(
                world.Props.Count,
                floraRoot,
                worldSpriteMaterial,
                floraInstance,
                atlas);

            view.Populate(world.Props, parent);
            return view;
        }

        public void ApplyViewHeight(float viewHeight)
        {
            float fade = 1f - Mathf.Clamp01((viewHeight - 50f) / 14f);
            if (_floraMaterial != null)
            {
                _floraMaterial.SetFloat(FadeId, fade);
            }

            if (_floraRoot != null)
            {
                bool active = fade > 0f;
                if (_floraRoot.activeSelf != active)
                {
                    _floraRoot.SetActive(active);
                }
            }
        }

        private void Populate(IReadOnlyList<PropInstance> props, Transform parent)
        {
            int[] order = PropOrder.BackToFront(props);
            int lipRank = 0;
            int propRank = 0;

            for (int i = 0; i < order.Length; i++)
            {
                int propIndex = order[i];
                PropInstance prop = props[propIndex];
                PropVariantSpec spec = PropCatalog.Get(prop.Variant);
                bool isLip = prop.Variant == PropCatalog.BankLip;
                int sortingOrder = isLip ? DrawOrder.Lips + lipRank++ : DrawOrder.Props + propRank++;
                Vector3 pos = ComputePlanePosition(prop);

                _propRenderers[propIndex] = CreatePropRenderer(prop, spec, sortingOrder, pos, parent);

                if (spec.ShadowWidthCm > 0)
                {
                    SpriteRenderer shadow = CreateShadowRenderer(spec.ShadowWidthCm, prop.ScalePct, pos, parent);
                    _shadowByPropIndex[propIndex] = shadow;
                    _shadowRenderers.Add(shadow);
                }
            }
        }

        private SpriteRenderer CreatePropRenderer(
            PropInstance prop,
            PropVariantSpec spec,
            int sortingOrder,
            Vector3 pos,
            Transform nonFloraParent)
        {
            bool isFlora = spec.Family == PropFamily.Flora;
            Transform targetParent = isFlora ? _floraRoot.transform : nonFloraParent;

            var go = new GameObject(spec.Name);
            go.layer = DrawOrder.WorldLayer;
            if (targetParent != null)
            {
                go.transform.SetParent(targetParent, false);
            }

            float scale = prop.ScalePct / 100f;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(scale, scale, scale);

            float tone = prop.TonePct / 100f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _variantSprites[prop.Variant];
            sr.color = new Color(tone, tone, tone, 1f);
            sr.flipX = false;
            sr.flipY = false;
            sr.sharedMaterial = isFlora ? _floraMaterial : _spriteMaterial;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        private SpriteRenderer CreateShadowRenderer(
            int shadowWidthCm,
            ushort scalePct,
            Vector3 pos,
            Transform parent)
        {
            var go = new GameObject("shadow_soft");
            go.layer = DrawOrder.WorldLayer;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            float scale = (shadowWidthCm * (float)scalePct) / 40000f;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(scale, scale, scale);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _shadowSprite;
            sr.color = Color.white;
            sr.flipX = false;
            sr.flipY = false;
            sr.sharedMaterial = _spriteMaterial;
            sr.sortingOrder = DrawOrder.Shadows;
            return sr;
        }

        private static Vector3 ComputePlanePosition(PropInstance prop)
        {
            float u = prop.XCm / 100f;
            float v = Projection.PlaneVCm(prop.ZCm, prop.YCm) / 100f;
            return new Vector3(u, v, 0f);
        }

        private static Sprite[] LoadVariantSprites(SpriteAtlas atlas)
        {
            var sprites = new Sprite[PropCatalog.VariantCount];
            if (atlas == null)
            {
                return sprites;
            }

            for (int i = 0; i < PropCatalog.VariantCount; i++)
            {
                sprites[i] = atlas.GetSprite(PropCatalog.Variants[i].Name);
            }

            return sprites;
        }

        private static Material CreateFloraMaterialInstance(Material source)
        {
            if (source == null)
            {
                return null;
            }

            var instance = new Material(source)
            {
                name = source.name,
            };
            instance.SetFloat(FadeId, 1f);
            return instance;
        }
    }
}
