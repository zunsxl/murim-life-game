using System;
using System.Collections;
using System.Collections.Generic;
using Murim.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Murim.Tests.PlayMode
{
    [TestFixture]
    public sealed class PresentationTests
    {
        private static readonly HashSet<string> AllowedShaderNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Shader Graphs/Terrain",
            "Shader Graphs/Water",
            "Shader Graphs/WorldSprite",
            "Shader Graphs/Backdrop",
        };

        private Func<IReadOnlyList<string>> _originalArgs;

        [SetUp]
        public void SetUp()
        {
            _originalArgs = SeedSource.GetCommandLineArgs;
        }

        [TearDown]
        public void TearDown()
        {
            SeedSource.GetCommandLineArgs = _originalArgs;
        }

        // T-ENG-07
        [UnityTest]
        public IEnumerator T_ENG_07_CameraAtmosphereBackdropAndStrictly2DRules_MatchSpec()
        {
            SeedSource.GetCommandLineArgs = () => new[] { "MurimLifeGame.exe", "-seed", "1" };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            Assert.That(cameras.Length, Is.EqualTo(1));
            Camera cam = cameras[0];
            Assert.That(cam.orthographic, Is.True);
            Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
            Assert.That(cam.nearClipPlane, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(cam.farClipPlane, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(cam.orthographicSize, Is.EqualTo(22f).Within(1e-5f));
            AssertColorNear(cam.backgroundColor, GameBootstrap.HazeColor);

            // Five atmosphere globals (§3.3.1)
            AssertColorNear(Shader.GetGlobalColor("_MurimWarmTint"), GameBootstrap.WarmTintColor);
            AssertColorNear(Shader.GetGlobalColor("_MurimHazeColor"), GameBootstrap.HazeColor);
            Assert.That(Shader.GetGlobalFloat("_MurimHazeMax"), Is.EqualTo(0.45f).Within(1e-5f));
            float expectedEndV = boot.World.PlaneCrestCm / 100f;
            Assert.That(Shader.GetGlobalFloat("_MurimHazeEndV"), Is.EqualTo(expectedEndV).Within(1e-4f));
            Assert.That(Shader.GetGlobalFloat("_MurimHazeStartV"), Is.EqualTo(expectedEndV - 110f).Within(1e-4f));

            // No lights, reflection probes, or volumes
            Assert.That(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<Light2D>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None), Is.Empty);

            // Every world renderer is SpriteRenderer or flat MeshRenderer using one of the four Shader Graphs
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(71));
            foreach (Renderer r in renderers)
            {
                bool isAllowedType = r is SpriteRenderer || r is MeshRenderer;
                Assert.That(isAllowedType, Is.True, "Unexpected renderer type: " + r.GetType().Name);
                if (r is MeshRenderer mr)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    Assert.That(mf, Is.Not.Null);
                    Assert.That(mf.sharedMesh, Is.Not.Null);
                    Assert.That(mf.sharedMesh.bounds.size.z, Is.EqualTo(0f).Within(1e-5f));
                }

                Material mat = r.sharedMaterial;
                Assert.That(mat, Is.Not.Null);
                Assert.That(mat.shader, Is.Not.Null);
                Assert.That(mat.shader.isSupported, Is.True, "Unsupported shader: " + mat.shader.name);
                Assert.That(AllowedShaderNames.Contains(mat.shader.name), Is.True, "Unexpected shader: " + mat.shader.name);
            }

            // Backdrop covers full viewport at 4:3, 16:9, 32:9
            float[] aspects = { 4f / 3f, 16f / 9f, 32f / 9f };
            float originalAspect = cam.aspect;
            foreach (float aspect in aspects)
            {
                cam.aspect = aspect;
                yield return null;

                Bounds quadBounds = boot.BackdropRenderer.bounds;
                float halfH = cam.orthographicSize;
                float halfW = halfH * aspect;
                Vector3 camPos = cam.transform.position;
                Assert.That(quadBounds.min.x, Is.LessThanOrEqualTo(camPos.x - halfW + 1e-3f));
                Assert.That(quadBounds.max.x, Is.GreaterThanOrEqualTo(camPos.x + halfW - 1e-3f));
                Assert.That(quadBounds.min.y, Is.LessThanOrEqualTo(camPos.y - halfH + 1e-3f));
                Assert.That(quadBounds.max.y, Is.GreaterThanOrEqualTo(camPos.y + halfH - 1e-3f));
            }

            cam.aspect = originalAspect;
        }

        private static void AssertColorNear(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-3f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-3f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-3f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(1e-3f));
        }
    }
}
