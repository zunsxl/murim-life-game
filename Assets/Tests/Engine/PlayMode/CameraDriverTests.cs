using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Murim.Core.Camera;
using Murim.Core.World;
using Murim.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Murim.Tests.PlayMode
{
    [TestFixture]
    public sealed class CameraDriverTests
    {
        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Func<IReadOnlyList<string>> _originalArgs;
        private ConfigFileAccess _originalFileAccess;

        [SetUp]
        public void SetUp()
        {
            _originalArgs = SeedSource.GetCommandLineArgs;
            _originalFileAccess = JsonConfigLoader.FileAccess;
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
            JsonConfigLoader.FileAccess = _originalFileAccess;
        }

        // T-ENG-08
        [UnityTest]
        public IEnumerator T_ENG_08_PanKeysDragScrollResetIgnoredInputsAndConfigOverride()
        {
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);
            yield return MovePointerToWindowCenter();

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            Camera cam = boot.WorldCamera;
            CameraRigDriver driver = boot.CameraDriver;
            Assert.That(driver, Is.Not.Null);
            CameraRig rig = driver.Rig;
            Assert.That(rig, Is.Not.Null);

            // 1. All 8 pan keys move the view centre in the mapped direction and keep rotation identity.
            yield return AssertPanKeyMoves(_keyboard.wKey, cam, rig, 0, +1);
            yield return AssertPanKeyMoves(_keyboard.upArrowKey, cam, rig, 0, +1);
            yield return AssertPanKeyMoves(_keyboard.sKey, cam, rig, 0, -1);
            yield return AssertPanKeyMoves(_keyboard.downArrowKey, cam, rig, 0, -1);
            yield return AssertPanKeyMoves(_keyboard.aKey, cam, rig, -1, 0);
            yield return AssertPanKeyMoves(_keyboard.leftArrowKey, cam, rig, -1, 0);
            yield return AssertPanKeyMoves(_keyboard.dKey, cam, rig, +1, 0);
            yield return AssertPanKeyMoves(_keyboard.rightArrowKey, cam, rig, +1, 0);

            // 2. Right-drag moves the camera opposite to pointer delta and keeps rotation identity.
            yield return ResetWithKey(_keyboard.homeKey, cam, rig, boot.World);
            float dragStartCu = rig.Cu;
            float dragStartCv = rig.Cv;
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Press(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.35f, Screen.height * 0.35f));
            yield return WaitForLateUpdate();
            Assert.That(rig.Cu, Is.GreaterThan(dragStartCu + 0.5f));
            Assert.That(rig.Cv, Is.GreaterThan(dragStartCv + 0.5f));
            Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
            _input.Release(_mouse.rightButton);
            yield return WaitUntilSettled(rig);

            // 3. Scroll: one detent = one 15 % step; orthographicSize == H / 2; _MurimCoarseBlend tracks H.
            yield return ResetWithKey(_keyboard.rKey, cam, rig, boot.World);
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Set(_mouse.scroll, new Vector2(0f, 1f));
            yield return WaitForLateUpdate();
            Assert.That(rig.TargetViewHeight, Is.EqualTo(44f / 1.15f).Within(1e-3f));

            float scrollElapsed = 0f;
            while (scrollElapsed < 0.25f)
            {
                yield return null;
                scrollElapsed += Time.unscaledDeltaTime;
                Assert.That(cam.orthographicSize, Is.EqualTo(rig.H * 0.5f).Within(1e-4f));
                Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
            }

            Assert.That(rig.H, Is.LessThan(43.5f));
            float expectedBlend = Mathf.Clamp01((rig.H - 30f) / 40f) * 0.5f;
            Assert.That(Shader.GetGlobalFloat("_MurimCoarseBlend"), Is.EqualTo(expectedBlend).Within(1e-4f));

            _input.Set(_mouse.scroll, new Vector2(0f, -1f));
            yield return WaitForLateUpdate();
            Assert.That(rig.TargetViewHeight, Is.EqualTo(44f).Within(1e-3f));

            // 4. Home and R reset back to vista.
            yield return ResetWithKey(_keyboard.homeKey, cam, rig, boot.World);

            // 5. Left click, middle drag, and resting pointer on all 4 window edges for 2 s leave pose unchanged.
            yield return WaitUntilSettled(rig);
            CameraPose frozen = rig.Pose;

            _input.Press(_mouse.leftButton);
            yield return null;
            _input.Release(_mouse.leftButton);
            yield return null;
            AssertPoseEqual(rig.Pose, frozen, 1e-5f);

            _input.Press(_mouse.middleButton);
            yield return null;
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.25f, Screen.height * 0.75f));
            yield return null;
            _input.Release(_mouse.middleButton);
            yield return null;
            AssertPoseEqual(rig.Pose, frozen, 1e-5f);

            Vector2[] edgePositions =
            {
                new Vector2(0f, Screen.height * 0.5f),
                new Vector2(Screen.width, Screen.height * 0.5f),
                new Vector2(Screen.width * 0.5f, 0f),
                new Vector2(Screen.width * 0.5f, Screen.height),
            };
            foreach (Vector2 edgePos in edgePositions)
            {
                _input.Move(_mouse.position, edgePos);
                float elapsed = 0f;
                while (elapsed < 2f)
                {
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                    AssertPoseEqual(rig.Pose, frozen, 1e-5f);
                    Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
                }
            }

            // 6. camera.json override via the seam reaches CameraConfig.
            const string cameraJson =
                "{\"schemaVersion\":1,\"minViewHeightM\":10,\"maxViewHeightM\":80," +
                "\"defaultViewHeightM\":32,\"panSpeedPerViewHeight\":1.5," +
                "\"panAccelTimeS\":0.2,\"panStopTimeS\":0.3,\"zoomStepPct\":25," +
                "\"zoomSmoothTimeS\":0.15,\"resetSmoothTimeS\":0.4}";
            byte[] camBytes = Encoding.UTF8.GetBytes(cameraJson);
            JsonConfigLoader.FileAccess = fileName =>
            {
                if (string.Equals(fileName, JsonConfigLoader.CameraConfigFileName, StringComparison.Ordinal))
                {
                    return new ConfigFileProbe(true, camBytes.Length, () => camBytes);
                }

                return new ConfigFileProbe(false, 0, null);
            };

            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);
            boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot.CameraConfig.MinViewHeightM, Is.EqualTo(10f).Within(1e-5f));
            Assert.That(boot.CameraConfig.MaxViewHeightM, Is.EqualTo(80f).Within(1e-5f));
            Assert.That(boot.CameraConfig.DefaultViewHeightM, Is.EqualTo(32f).Within(1e-5f));
            Assert.That(boot.CameraConfig.PanSpeedPerViewHeight, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(boot.CameraConfig.PanAccelTimeS, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(boot.CameraConfig.PanStopTimeS, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(boot.CameraConfig.ZoomStepPct, Is.EqualTo(25f).Within(1e-5f));
            Assert.That(boot.CameraConfig.ZoomSmoothTimeS, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(boot.CameraConfig.ResetSmoothTimeS, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(boot.WorldCamera.orthographicSize, Is.EqualTo(16f).Within(1e-4f));
        }

        // T-ENG-09
        [UnityTest]
        public IEnumerator T_ENG_09_FocusLostPauseAndPointerOutside_StopMotionAndRequireRepress()
        {
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);
            yield return MovePointerToWindowCenter();

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            CameraRigDriver driver = boot.CameraDriver;
            CameraRig rig = driver.Rig;

            // 1. Focus lost while pan key held.
            _input.Press(_keyboard.dKey);
            yield return AdvanceUnscaledSeconds(0.15f);
            driver.OnApplicationFocus(false);
            yield return WaitForLateUpdate();
            CameraPose stoppedPose = rig.Pose;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                AssertPoseEqual(rig.Pose, stoppedPose, 1e-6f);
            }

            _input.Release(_keyboard.dKey);
            yield return WaitForLateUpdate();
            _input.Press(_keyboard.dKey);
            yield return AdvanceUnscaledSeconds(0.15f);
            Assert.That(rig.Cu, Is.GreaterThan(stoppedPose.Cu + 0.1f));
            _input.Release(_keyboard.dKey);
            yield return WaitUntilSettled(rig);

            // 2. Application pause while right-drag held.
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Press(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.45f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            driver.OnApplicationPause(true);
            yield return WaitForLateUpdate();
            CameraPose pausedPose = rig.Pose;
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.35f, Screen.height * 0.5f));
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                AssertPoseEqual(rig.Pose, pausedPose, 1e-6f);
            }

            _input.Release(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Press(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.4f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            Assert.That(rig.Cu, Is.GreaterThan(pausedPose.Cu + 0.5f));
            _input.Release(_mouse.rightButton);
            yield return WaitUntilSettled(rig);

            // 3a. Pointer moves outside window while pan key held.
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Press(_keyboard.wKey);
            yield return AdvanceUnscaledSeconds(0.15f);
            _input.Move(_mouse.position, new Vector2(-20f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            CameraPose exitKeyPose = rig.Pose;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                AssertPoseEqual(rig.Pose, exitKeyPose, 1e-6f);
            }

            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            _input.Release(_keyboard.wKey);
            yield return WaitForLateUpdate();
            _input.Press(_keyboard.wKey);
            yield return AdvanceUnscaledSeconds(0.15f);
            Assert.That(rig.Cv, Is.GreaterThan(exitKeyPose.Cv + 0.1f));
            _input.Release(_keyboard.wKey);
            yield return WaitUntilSettled(rig);

            // 3b. Pointer moves outside window while right-drag held.
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Press(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.55f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width + 20f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            CameraPose exitDragPose = rig.Pose;
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                AssertPoseEqual(rig.Pose, exitDragPose, 1e-6f);
            }

            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            _input.Release(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Press(_mouse.rightButton);
            yield return WaitForLateUpdate();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.4f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
            Assert.That(rig.Cu, Is.GreaterThan(exitDragPose.Cu + 0.5f));
            _input.Release(_mouse.rightButton);
        }

        // T-ENG-10
        [UnityTest]
        public IEnumerator T_ENG_10_CornerPositionsAtWidestZoom_ReclampAcrossAspectsAndCoverBackdrop()
        {
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);
            yield return MovePointerToWindowCenter();

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Camera cam = boot.WorldCamera;
            CameraRig rig = boot.CameraDriver.Rig;
            var frame = new ViewFrame(
                boot.World.PlaneBottomCm,
                boot.World.PlaneCrestCm,
                boot.World.VistaUCm,
                boot.World.VistaVCm);

            // Zoom out to widest view height (88 m).
            for (int i = 0; i < 4; i++)
            {
                _input.Set(_mouse.scroll, new Vector2(0f, -4f));
                yield return null;
            }

            float zoomElapsed = 0f;
            while (rig.H < 87.99f && zoomElapsed < 3f)
            {
                yield return null;
                zoomElapsed += Time.unscaledDeltaTime;
            }

            Assert.That(rig.H, Is.EqualTo(88f).Within(1e-2f));

            float originalAspect = cam.aspect;
            float[] aspects = { 4f / 3f, 16f / 9f, 21f / 9f, 32f / 9f };
            (KeyControl KeyU, KeyControl KeyV)[] corners =
            {
                (_keyboard.aKey, _keyboard.sKey),
                (_keyboard.dKey, _keyboard.sKey),
                (_keyboard.aKey, _keyboard.wKey),
                (_keyboard.dKey, _keyboard.wKey),
            };

            foreach ((KeyControl keyU, KeyControl keyV) in corners)
            {
                _input.Press(keyU);
                _input.Press(keyV);
                float pushElapsed = 0f;
                while (pushElapsed < 6f && (pushElapsed < 0.2f || rig.Vu != 0f || rig.Vv != 0f))
                {
                    yield return null;
                    pushElapsed += Time.unscaledDeltaTime;
                }

                _input.Release(keyU);
                _input.Release(keyV);
                yield return WaitUntilSettled(rig);

                foreach (float aspect in aspects)
                {
                    cam.aspect = aspect;
                    yield return null;

                    CameraPose pose = rig.Pose;
                    Assert.That(float.IsFinite(pose.Cu), Is.True);
                    Assert.That(float.IsFinite(pose.Cv), Is.True);
                    Assert.That(float.IsFinite(pose.H), Is.True);

                    float hMinEff = rig.ComputeHMinEff(Screen.height);
                    float hMaxEff = rig.ComputeHMaxEff(aspect, hMinEff);
                    Assert.That(pose.H, Is.InRange(hMinEff - 1e-4f, hMaxEff + 1e-4f));

                    float halfW = pose.H * aspect * 0.5f;
                    float halfH = pose.H * 0.5f;
                    Assert.That(pose.Cu - halfW, Is.GreaterThanOrEqualTo(-1e-4f));
                    Assert.That(pose.Cu + halfW, Is.LessThanOrEqualTo(ViewFrame.WorldWidthM + 1e-4f));
                    Assert.That(pose.Cv - halfH, Is.GreaterThanOrEqualTo(frame.BottomV - 1e-4f));
                    Assert.That(
                        pose.Cv + halfH,
                        Is.LessThanOrEqualTo(frame.CrestV + ViewFrame.SkyFraction * pose.H + 1e-4f));

                    Bounds quadBounds = boot.BackdropRenderer.bounds;
                    Vector3 camPos = cam.transform.position;
                    Assert.That(quadBounds.min.x, Is.LessThanOrEqualTo(camPos.x - halfW + 1e-3f));
                    Assert.That(quadBounds.max.x, Is.GreaterThanOrEqualTo(camPos.x + halfW - 1e-3f));
                    Assert.That(quadBounds.min.y, Is.LessThanOrEqualTo(camPos.y - halfH + 1e-3f));
                    Assert.That(quadBounds.max.y, Is.GreaterThanOrEqualTo(camPos.y + halfH - 1e-3f));
                }

                cam.aspect = originalAspect;
                yield return null;
            }
        }

        private IEnumerator MovePointerToWindowCenter()
        {
            InputSystem.Update();
            _input.Move(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            yield return WaitForLateUpdate();
        }

        private static IEnumerator WaitForLateUpdate()
        {
            yield return null;
            yield return null;
        }

        private IEnumerator AssertPanKeyMoves(KeyControl key, Camera cam, CameraRig rig, int dirU, int dirV)
        {
            float startCu = rig.Cu;
            float startCv = rig.Cv;

            _input.Press(key);
            yield return AdvanceUnscaledSeconds(0.15f);
            _input.Release(key);

            if (dirU != 0)
            {
                Assert.That((rig.Cu - startCu) * dirU, Is.GreaterThan(0.1f));
            }

            if (dirV != 0)
            {
                Assert.That((rig.Cv - startCv) * dirV, Is.GreaterThan(0.1f));
            }

            Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
            yield return WaitUntilSettled(rig);
        }

        private IEnumerator ResetWithKey(KeyControl key, Camera cam, CameraRig rig, ValleyWorld world)
        {
            _input.Press(key);
            yield return WaitForLateUpdate();
            _input.Release(key);

            float elapsed = 0f;
            while (rig.Mode == CameraMode.Resetting && elapsed < 6f)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                Assert.That(cam.transform.rotation, Is.EqualTo(Quaternion.identity));
            }

            Assert.That(rig.Mode, Is.EqualTo(CameraMode.Free));
            Assert.That(rig.Cu, Is.EqualTo(world.VistaUCm / 100f).Within(1e-3f));
            Assert.That(rig.Cv, Is.EqualTo(world.VistaVCm / 100f).Within(1e-3f));
            Assert.That(rig.H, Is.EqualTo(44f).Within(1e-3f));
        }

        private static IEnumerator WaitUntilSettled(CameraRig rig)
        {
            float elapsed = 0f;
            while (elapsed < 4f)
            {
                if (rig.Vu == 0f && rig.Vv == 0f && Mathf.Abs(rig.H - rig.TargetViewHeight) < 1e-4f)
                {
                    yield break;
                }

                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        private static IEnumerator AdvanceUnscaledSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        private static void AssertPoseEqual(CameraPose actual, CameraPose expected, float tol)
        {
            Assert.That(actual.Cu, Is.EqualTo(expected.Cu).Within(tol));
            Assert.That(actual.Cv, Is.EqualTo(expected.Cv).Within(tol));
            Assert.That(actual.H, Is.EqualTo(expected.H).Within(tol));
        }
    }
}
