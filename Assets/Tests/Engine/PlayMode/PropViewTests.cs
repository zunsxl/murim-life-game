using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Murim.Core.World;
using Murim.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.U2D;
using AllocIs = UnityEngine.TestTools.Constraints.Is;
using Is = NUnit.Framework.Is;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Murim.Tests.PlayMode
{
    [TestFixture]
    public sealed class PropViewTests
    {
        private static readonly HashSet<string> ForbiddenModelExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".fbx", ".obj", ".blend", ".dae", ".gltf", ".glb", ".3ds",
            };

        private Func<IReadOnlyList<string>> _originalArgs;
        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;

        [SetUp]
        public void SetUp()
        {
            _originalArgs = SeedSource.GetCommandLineArgs;
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe", "-seed", "1" };

            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.Update();
        }

        [TearDown]
        public void TearDown()
        {
            _input.TearDown();
            SeedSource.GetCommandLineArgs = _originalArgs;
        }

        // T-ENG-06
        [UnityTest]
        public IEnumerator T_ENG_06_PropsLipsShadowsAndFloraFade_MatchSpec()
        {
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            PropView propView = boot.PropView;
            Assert.That(propView, Is.Not.Null);

            SpriteAtlas atlas = Resources.Load<SpriteAtlas>("Art/Props/WorldProps");
            Assert.That(atlas, Is.Not.Null);
            Texture2D atlasPageTexture = ResolveSingleAtlasPageTexture(atlas);

            Material worldFloraAsset = Resources.Load<Material>("Materials/WorldFlora");
            Assert.That(worldFloraAsset, Is.Not.Null);
            Assert.That(worldFloraAsset.GetFloat("_Fade"), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(propView.FloraMaterial, Is.Not.Null);
            Assert.That(propView.FloraMaterial, Is.Not.SameAs(worldFloraAsset));

            ValleyWorld world = boot.World;
            Assert.That(propView.PropRenderers.Count, Is.EqualTo(world.Props.Count));

            int expectedShadowCount = VerifyPropsAndShadows(world, propView, atlasPageTexture, worldFloraAsset);
            Assert.That(propView.ShadowRenderers.Count, Is.EqualTo(expectedShadowCount));

            SpriteRenderer[] allSprites = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(allSprites.Length, Is.EqualTo(world.Props.Count + expectedShadowCount));

            VerifyBackToFrontSortOrders(world, propView);
            VerifyFloraViewHeightFade(propView, worldFloraAsset);
        }

        // T-ENG-11
        [UnityTest]
        public IEnumerator T_ENG_11_LateUpdateZeroGcAndSceneAndAssetRules_Hold()
        {
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            CameraRigDriver driver = boot.CameraDriver;
            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.Blocked, Is.False);

            MethodInfo lateUpdateMethod = typeof(CameraRigDriver).GetMethod(
                "LateUpdate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lateUpdateMethod, Is.Not.Null);
            var lateUpdate = (TestDelegate)Delegate.CreateDelegate(typeof(TestDelegate), driver, lateUpdateMethod);

            InputSystem.Update();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return null;
            yield return null;

            // 120 frames idle
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                Assert.That(lateUpdate, AllocIs.Not.AllocatingGCMemory());
            }

            // 120 frames panning and zooming (crossing flora fade thresholds)
            _input.Press(_keyboard.dKey);
            _input.Press(_keyboard.wKey);
            for (int i = 0; i < 120; i++)
            {
                if (i < 60 && i % 10 == 0)
                {
                    _input.Set(_mouse.scroll, new Vector2(0f, -2f));
                }
                else if (i >= 60 && i % 10 == 0)
                {
                    _input.Set(_mouse.scroll, new Vector2(0f, 2f));
                }
                else
                {
                    _input.Set(_mouse.scroll, Vector2.zero);
                }

                InputSystem.Update();
                Assert.That(lateUpdate, AllocIs.Not.AllocatingGCMemory());
                yield return null;
            }

            _input.Release(_keyboard.dKey);
            _input.Release(_keyboard.wKey);

            VerifySceneHasNoForbiddenComponents();
            VerifyAssetsHaveNoModels();
        }

        private static Texture2D ResolveSingleAtlasPageTexture(SpriteAtlas atlas)
        {
            Texture2D pageTexture = null;
            for (int v = 0; v < PropCatalog.VariantCount; v++)
            {
                string name = PropCatalog.Variants[v].Name;
                Sprite sprite = atlas.GetSprite(name);
                Assert.That(sprite, Is.Not.Null, "Missing atlas sprite: " + name);
                Assert.That(sprite.texture, Is.Not.Null);
                if (pageTexture == null)
                {
                    pageTexture = sprite.texture;
                }
                else
                {
                    Assert.That(sprite.texture, Is.SameAs(pageTexture), "Sprite not packed on single atlas page: " + name);
                }
            }

            Sprite shadow = atlas.GetSprite("shadow_soft");
            Assert.That(shadow, Is.Not.Null, "Missing atlas sprite: shadow_soft");
            Assert.That(shadow.texture, Is.SameAs(pageTexture));

            Texture2D unpackedSource = Resources.Load<Texture2D>("Art/Props/Pines/pine_ancient");
            Assert.That(unpackedSource, Is.Not.Null);
            Assert.That(pageTexture, Is.Not.SameAs(unpackedSource), "Atlas texture must be the packed page, not raw PNG");
            return pageTexture;
        }

        private static string StripCloneSuffix(string spriteName)
        {
            const string cloneSuffix = "(Clone)";
            if (!string.IsNullOrEmpty(spriteName) && spriteName.EndsWith(cloneSuffix, StringComparison.Ordinal))
            {
                return spriteName.Substring(0, spriteName.Length - cloneSuffix.Length);
            }

            return spriteName;
        }

        private static int VerifyPropsAndShadows(
            ValleyWorld world,
            PropView propView,
            Texture2D atlasPageTexture,
            Material worldFloraAsset)
        {
            int expectedShadowCount = 0;
            for (int i = 0; i < world.Props.Count; i++)
            {
                PropInstance prop = world.Props[i];
                PropVariantSpec spec = PropCatalog.Get(prop.Variant);
                SpriteRenderer sr = propView.PropRenderers[i];

                Assert.That(sr, Is.Not.Null);
                Assert.That(sr.gameObject.layer, Is.EqualTo(DrawOrder.WorldLayer));
                Assert.That(sr.sprite, Is.Not.Null);
                Assert.That(StripCloneSuffix(sr.sprite.name), Is.EqualTo(spec.Name));
                Assert.That(sr.sprite.texture, Is.SameAs(atlasPageTexture));

                float expectedX = prop.XCm / 100f;
                float expectedY = Projection.PlaneVCm(prop.ZCm, prop.YCm) / 100f;
                Assert.That(sr.transform.position.x, Is.EqualTo(expectedX).Within(1e-4f));
                Assert.That(sr.transform.position.y, Is.EqualTo(expectedY).Within(1e-4f));
                Assert.That(sr.transform.position.z, Is.EqualTo(0f).Within(1e-5f));

                float expectedScale = prop.ScalePct / 100f;
                Assert.That(sr.transform.localScale.x, Is.EqualTo(expectedScale).Within(1e-4f));
                Assert.That(sr.transform.localScale.y, Is.EqualTo(expectedScale).Within(1e-4f));
                Assert.That(sr.transform.localScale.z, Is.EqualTo(expectedScale).Within(1e-4f));
                Assert.That(sr.transform.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(sr.flipX, Is.False);
                Assert.That(sr.flipY, Is.False);

                float t = prop.TonePct / 100f;
                Assert.That(sr.color.r, Is.EqualTo(t).Within(1e-4f));
                Assert.That(sr.color.g, Is.EqualTo(t).Within(1e-4f));
                Assert.That(sr.color.b, Is.EqualTo(t).Within(1e-4f));
                Assert.That(sr.color.a, Is.EqualTo(1f).Within(1e-4f));

                if (spec.Family == PropFamily.Flora)
                {
                    Assert.That(sr.sharedMaterial, Is.SameAs(propView.FloraMaterial));
                    Assert.That(sr.sharedMaterial, Is.Not.SameAs(worldFloraAsset));
                    Assert.That(sr.transform.parent, Is.SameAs(propView.FloraRoot.transform));
                }
                else
                {
                    Assert.That(sr.sharedMaterial, Is.SameAs(propView.SpriteMaterial));
                    Assert.That(sr.sharedMaterial.GetFloat("_Fade"), Is.EqualTo(1f).Within(1e-5f));
                }

                SpriteRenderer shadow = propView.GetShadowForProp(i);
                bool expectsShadow = spec.Family == PropFamily.Pine
                    || spec.Family == PropFamily.Bamboo
                    || spec.Family == PropFamily.Rock;
                if (expectsShadow)
                {
                    expectedShadowCount++;
                    float expectedShadowScale = (spec.ShadowWidthCm * (float)prop.ScalePct) / 40000f;
                    AssertShadowRenderer(
                        shadow,
                        sr.transform.position,
                        expectedShadowScale,
                        propView.SpriteMaterial,
                        atlasPageTexture);
                }
                else
                {
                    Assert.That(shadow, Is.Null);
                }
            }

            return expectedShadowCount;
        }

        private static void AssertShadowRenderer(
            SpriteRenderer shadow,
            Vector3 propPos,
            float expectedScale,
            Material expectedMaterial,
            Texture2D atlasPageTexture)
        {
            Assert.That(shadow, Is.Not.Null);
            Assert.That(shadow.gameObject.layer, Is.EqualTo(DrawOrder.WorldLayer));
            Assert.That(shadow.sprite, Is.Not.Null);
            Assert.That(StripCloneSuffix(shadow.sprite.name), Is.EqualTo("shadow_soft"));
            Assert.That(shadow.sprite.texture, Is.SameAs(atlasPageTexture));
            Assert.That(shadow.transform.position.x, Is.EqualTo(propPos.x).Within(1e-4f));
            Assert.That(shadow.transform.position.y, Is.EqualTo(propPos.y).Within(1e-4f));
            Assert.That(shadow.transform.position.z, Is.EqualTo(0f).Within(1e-5f));

            Assert.That(shadow.transform.localScale.x, Is.EqualTo(expectedScale).Within(1e-5f));
            Assert.That(shadow.transform.localScale.y, Is.EqualTo(expectedScale).Within(1e-5f));
            Assert.That(shadow.transform.localScale.z, Is.EqualTo(expectedScale).Within(1e-5f));
            Assert.That(shadow.transform.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(shadow.flipX, Is.False);
            Assert.That(shadow.flipY, Is.False);
            Assert.That(shadow.color, Is.EqualTo(Color.white));
            Assert.That(shadow.sharedMaterial, Is.SameAs(expectedMaterial));
            Assert.That(shadow.sortingOrder, Is.EqualTo(DrawOrder.Shadows));
        }

        private static void VerifyBackToFrontSortOrders(ValleyWorld world, PropView propView)
        {
            var nonLipOrders = new HashSet<int>();
            var lipOrders = new HashSet<int>();
            int[] sorted = PropOrder.BackToFront(world.Props);

            int prevNonLipZ = int.MaxValue;
            int prevNonLipOrder = int.MinValue;

            for (int k = 0; k < sorted.Length; k++)
            {
                int propIndex = sorted[k];
                PropInstance prop = world.Props[propIndex];
                int order = propView.PropRenderers[propIndex].sortingOrder;

                if (prop.Variant == PropCatalog.BankLip)
                {
                    Assert.That(order, Is.GreaterThanOrEqualTo(DrawOrder.Lips));
                    Assert.That(order, Is.LessThan(DrawOrder.Shadows));
                    Assert.That(lipOrders.Add(order), Is.True);
                }
                else
                {
                    Assert.That(order, Is.GreaterThanOrEqualTo(DrawOrder.Props));
                    Assert.That(nonLipOrders.Add(order), Is.True);
                    if (prevNonLipOrder != int.MinValue)
                    {
                        Assert.That(order, Is.GreaterThan(prevNonLipOrder));
                        if (prop.ZCm != prevNonLipZ)
                        {
                            Assert.That(prop.ZCm, Is.LessThan(prevNonLipZ));
                        }
                    }

                    prevNonLipZ = prop.ZCm;
                    prevNonLipOrder = order;
                }
            }
        }

        private static void VerifyFloraViewHeightFade(PropView propView, Material worldFloraAsset)
        {
            SpriteRenderer sampleFlora = null;
            for (int i = 0; i < propView.PropRenderers.Count; i++)
            {
                SpriteRenderer sr = propView.PropRenderers[i];
                if (sr.sharedMaterial == propView.FloraMaterial)
                {
                    sampleFlora = sr;
                    break;
                }
            }

            Assert.That(sampleFlora, Is.Not.Null);

            float[] heights = { 44f, 57f, 64f, 88f };
            float[] expectedFades = { 1f, 0.5f, 0f, 0f };
            bool[] expectedActive = { true, true, false, false };

            for (int i = 0; i < heights.Length; i++)
            {
                propView.ApplyViewHeight(heights[i]);
                Assert.That(sampleFlora.sharedMaterial.GetFloat("_Fade"), Is.EqualTo(expectedFades[i]).Within(1e-5f));
                Assert.That(worldFloraAsset.GetFloat("_Fade"), Is.EqualTo(1f).Within(1e-5f));
                Assert.That(propView.FloraRoot.activeSelf, Is.EqualTo(expectedActive[i]));
            }

            propView.ApplyViewHeight(44f);
        }

        private static void VerifySceneHasNoForbiddenComponents()
        {
            Assert.That(FindAll<AudioSource>(), Is.Empty);
            Assert.That(FindAll<AudioListener>(), Is.Empty);
            Assert.That(FindAll<Animator>(), Is.Empty);
            Assert.That(FindAll<Animation>(), Is.Empty);
            Assert.That(FindAll<ParticleSystem>(), Is.Empty);
            Assert.That(FindAll<Rigidbody>(), Is.Empty);
            Assert.That(FindAll<Rigidbody2D>(), Is.Empty);
            Assert.That(FindAll<Collider>(), Is.Empty);
            Assert.That(FindAll<Collider2D>(), Is.Empty);
            Assert.That(FindAll<Light>(), Is.Empty);
            Assert.That(FindAll<Light2D>(), Is.Empty);

            Canvas[] canvases = FindAll<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                Assert.That(canvases[i].renderMode, Is.Not.EqualTo(RenderMode.WorldSpace));
            }
        }

        private static T[] FindAll<T>() where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private static void VerifyAssetsHaveNoModels()
        {
#if UNITY_EDITOR
            string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets" });
            Assert.That(modelGuids, Is.Empty);
#endif
            string[] allFiles = Directory.GetFiles("Assets", "*", SearchOption.AllDirectories);
            for (int i = 0; i < allFiles.Length; i++)
            {
                string ext = Path.GetExtension(allFiles[i]);
                Assert.That(
                    ForbiddenModelExtensions.Contains(ext),
                    Is.False,
                    "Forbidden model file found: " + allFiles[i]);
            }
        }
    }
}
