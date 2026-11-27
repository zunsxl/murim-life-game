using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Murim.Core.Camera;
using Murim.View;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Murim.Tests.PlayMode
{
    [TestFixture]
    public sealed class SystemOverlayTests
    {
        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Func<IReadOnlyList<string>> _originalArgs;
        private Action _originalQuitAction;

        [SetUp]
        public void SetUp()
        {
            _originalArgs = SeedSource.GetCommandLineArgs;
            _originalQuitAction = SystemOverlay.QuitAction;
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
            SystemOverlay.QuitAction = _originalQuitAction;
            _input.TearDown();
            SeedSource.GetCommandLineArgs = _originalArgs;
        }

        // T-ENG-12
        [UnityTest]
        public IEnumerator T_ENG_12_SystemOverlay_OpensBlocksCameraRescalesAndHandlesResumeEscQuit()
        {
            yield return SceneManager.LoadSceneAsync("Valley", LoadSceneMode.Single);

            GameBootstrap boot = UnityEngine.Object.FindFirstObjectByType<GameBootstrap>();
            Assert.That(boot, Is.Not.Null);
            SystemOverlay overlay = boot.SystemOverlay;
            Assert.That(overlay, Is.Not.Null);
            CameraRigDriver driver = boot.CameraDriver;
            Assert.That(driver, Is.Not.Null);
            Assert.That(overlay.IsOpen, Is.False);
            Assert.That(driver.Blocked, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-5f));

            VerifyOverlayStructure(overlay);

            (int Width, int Height)[] sizes =
            {
                (1920, 1080),
                (960, 540),
                (800, 1200),
            };

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                foreach ((int width, int height) in sizes)
                {
                    yield return ApplyScreenSize(width, height, overlay);
                    yield return VerifyOverlayCycleAtCurrentSize(overlay, driver, width, height);
                }

                yield break;
            }

            // Headless batchmode (-nographics): run full interactive cycle at active screen rect,
            // and verify exact CanvasScaler + Panel centering/containment at all 3 target sizes.
            Rect liveRect = overlay.OverlayCanvas.pixelRect;
            int liveW = Mathf.RoundToInt(liveRect.width);
            int liveH = Mathf.RoundToInt(liveRect.height);
            yield return VerifyOverlayCycleAtCurrentSize(overlay, driver, liveW, liveH);

            foreach ((int width, int height) in sizes)
            {
                yield return VerifyPanelLayoutAtTargetSize(width, height);
            }
        }

        private IEnumerator VerifyOverlayCycleAtCurrentSize(
            SystemOverlay overlay,
            CameraRigDriver driver,
            int width,
            int height)
        {
            yield return MovePointerToCenter(width, height);

            // 1. Open with Esc
            yield return PressEscape();
            AssertOverlayOpenAndCentered(overlay, driver, width, height);

            // 2. While open: pan key, scroll, right-drag, Home leave camera pose unchanged; Time.timeScale stays 1
            yield return VerifyCameraBlockedWhileOpen(driver.Rig, width, height);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-5f));

            // 3. Click Resume -> closes immediately and camera input works on next frame
            yield return ClickButton(overlay.ResumeButton);
            Assert.That(overlay.IsOpen, Is.False);
            Assert.That(driver.Blocked, Is.False);
            yield return AssertCameraRespondsNextFrame(driver.Rig);

            // 4. Reopen with Esc, then press Esc -> closes immediately and camera input works on next frame
            yield return PressEscape();
            Assert.That(overlay.IsOpen, Is.True);
            Assert.That(driver.Blocked, Is.True);

            yield return PressEscape();
            Assert.That(overlay.IsOpen, Is.False);
            Assert.That(driver.Blocked, Is.False);
            yield return AssertCameraRespondsNextFrame(driver.Rig);

            // 5. Reopen with Esc, replace QuitAction, click Quit -> invoked exactly once
            yield return PressEscape();
            Assert.That(overlay.IsOpen, Is.True);

            int quitCount = 0;
            SystemOverlay.QuitAction = () => quitCount++;
            yield return ClickButton(overlay.QuitButton);
            Assert.That(quitCount, Is.EqualTo(1));

            // Close cleanly
            yield return PressEscape();
            Assert.That(overlay.IsOpen, Is.False);
            Assert.That(driver.Blocked, Is.False);
        }

        private static IEnumerator VerifyPanelLayoutAtTargetSize(int width, int height)
        {
            var rt = new RenderTexture(width, height, 16);
            var camGo = new GameObject("OverlayLayoutTestCam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.orthographic = true;

            GameObject prefab = Resources.Load<GameObject>("Prefabs/SystemOverlay");
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            EventSystem extraEs = instance.GetComponentInChildren<EventSystem>(true);
            if (extraEs != null)
            {
                UnityEngine.Object.DestroyImmediate(extraEs.gameObject);
            }

            SystemOverlay testOverlay = instance.GetComponent<SystemOverlay>();
            Canvas canvas = testOverlay.OverlayCanvas;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            testOverlay.SetOpen(true);

            yield return null;
            Canvas.ForceUpdateCanvases();

            Rect pixelRect = canvas.pixelRect;
            Assert.That(Mathf.RoundToInt(pixelRect.width), Is.EqualTo(width));
            Assert.That(Mathf.RoundToInt(pixelRect.height), Is.EqualTo(height));
            float expectedScale = Mathf.Max(width / 1920f, height / 1080f);
            Assert.That(canvas.scaleFactor, Is.EqualTo(expectedScale).Within(1e-3f));

            AssertPanelRectCenteredInScreen(testOverlay.PanelRect, cam, width, height);

            UnityEngine.Object.Destroy(instance);
            UnityEngine.Object.Destroy(camGo);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }

        private static void AssertPanelRectCenteredInScreen(
            RectTransform panelRect,
            Camera renderCamera,
            int width,
            int height)
        {
            var worldCorners = new Vector3[4];
            panelRect.GetWorldCorners(worldCorners);

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;

            for (int i = 0; i < 4; i++)
            {
                Vector3 screenPt = renderCamera != null
                    ? renderCamera.WorldToScreenPoint(worldCorners[i])
                    : worldCorners[i];
                if (screenPt.x < minX) minX = screenPt.x;
                if (screenPt.x > maxX) maxX = screenPt.x;
                if (screenPt.y < minY) minY = screenPt.y;
                if (screenPt.y > maxY) maxY = screenPt.y;
            }

            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;

            Assert.That(centerX, Is.EqualTo(width * 0.5f).Within(1f));
            Assert.That(centerY, Is.EqualTo(height * 0.5f).Within(1f));
            Assert.That(minX, Is.GreaterThanOrEqualTo(-0.5f));
            Assert.That(maxX, Is.LessThanOrEqualTo(width + 0.5f));
            Assert.That(minY, Is.GreaterThanOrEqualTo(-0.5f));
            Assert.That(maxY, Is.LessThanOrEqualTo(height + 0.5f));
        }

        private static void VerifyOverlayStructure(SystemOverlay overlay)
        {
            Canvas canvas = overlay.OverlayCanvas;
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(overlay.gameObject.layer, Is.EqualTo(DrawOrder.UiLayer));

            CanvasScaler scaler = overlay.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution.x, Is.EqualTo(1920f).Within(1e-2f));
            Assert.That(scaler.referenceResolution.y, Is.EqualTo(1080f).Within(1e-2f));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.Shrink));

            Assert.That(overlay.ScrimImage, Is.Not.Null);
            Assert.That(overlay.ScrimImage.raycastTarget, Is.True);
            Assert.That(overlay.ScrimImage.color.a, Is.EqualTo(0.35f).Within(1e-2f));

            Assert.That(overlay.PanelRect, Is.Not.Null);
            Assert.That(overlay.PanelRect.sizeDelta.x, Is.EqualTo(520f).Within(1e-2f));
            Assert.That(overlay.PanelRect.sizeDelta.y, Is.EqualTo(420f).Within(1e-2f));

            Assert.That(overlay.TitleText, Is.Not.Null);
            Assert.That(overlay.TitleText.text, Is.EqualTo("Paused"));
            Assert.That(overlay.TitleText.font, Is.Not.Null);
            Assert.That(overlay.TitleText.font.name, Is.EqualTo("OverlaySerif SDF"));
            Assert.That(overlay.TitleText.fontSize, Is.EqualTo(56f).Within(1e-2f));

            VerifyButton(overlay.ResumeButton, "Resume");
            VerifyButton(overlay.QuitButton, "Quit");

            EventSystem[] systems = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(systems.Length, Is.EqualTo(1));
            Assert.That(systems[0].GetComponent<InputSystemUIInputModule>(), Is.Not.Null);
            Assert.That(systems[0].GetComponent<StandaloneInputModule>(), Is.Null);
        }

        private static void VerifyButton(Button button, string expectedLabel)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.transition, Is.EqualTo(Selectable.Transition.SpriteSwap));
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            Assert.That(button.spriteState.highlightedSprite, Is.Not.Null);
            Assert.That(button.spriteState.pressedSprite, Is.Not.Null);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            Assert.That(label, Is.Not.Null);
            Assert.That(label.text, Is.EqualTo(expectedLabel));
            Assert.That(label.font, Is.Not.Null);
            Assert.That(label.font.name, Is.EqualTo("OverlaySerif SDF"));
            Assert.That(label.fontSize, Is.EqualTo(32f).Within(1e-2f));
        }

        private static void AssertOverlayOpenAndCentered(
            SystemOverlay overlay,
            CameraRigDriver driver,
            int width,
            int height)
        {
            Assert.That(overlay.IsOpen, Is.True);
            Assert.That(driver.Blocked, Is.True);
            Assert.That(overlay.PanelRect.gameObject.activeInHierarchy, Is.True);
            Assert.That(overlay.ScrimImage.gameObject.activeInHierarchy, Is.True);
            Assert.That(overlay.ResumeButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(overlay.QuitButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(overlay.TitleText.text, Is.EqualTo("Paused"));
            Assert.That(overlay.ResumeButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Resume"));
            Assert.That(overlay.QuitButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Quit"));

            Canvas.ForceUpdateCanvases();
            AssertPanelRectCenteredInScreen(overlay.PanelRect, null, width, height);
        }

        private IEnumerator VerifyCameraBlockedWhileOpen(CameraRig rig, int width, int height)
        {
            CameraPose frozen = rig.Pose;

            _input.Press(_keyboard.dKey);
            _input.Press(_keyboard.homeKey);
            _input.Set(_mouse.scroll, new Vector2(0f, 2f));
            _input.Move(_mouse.position, new Vector2(width * 0.5f, height * 0.5f));
            yield return null;

            _input.Press(_mouse.rightButton);
            yield return null;
            _input.Move(_mouse.position, new Vector2(width * 0.35f, height * 0.4f));

            for (int i = 0; i < 6; i++)
            {
                yield return null;
                AssertPoseEqual(rig.Pose, frozen, 1e-5f);
            }

            _input.Release(_keyboard.dKey);
            _input.Release(_keyboard.homeKey);
            _input.Release(_mouse.rightButton);
            _input.Set(_mouse.scroll, Vector2.zero);
            yield return null;
            AssertPoseEqual(rig.Pose, frozen, 1e-5f);
        }

        private IEnumerator AssertCameraRespondsNextFrame(CameraRig rig)
        {
            float startCu = rig.Cu;
            _input.Press(_keyboard.dKey);
            InputSystem.Update();
            yield return null;
            Assert.That(rig.Cu, Is.GreaterThan(startCu));
            _input.Release(_keyboard.dKey);
            InputSystem.Update();
            yield return WaitUntilSettled(rig);
        }

        private IEnumerator PressEscape()
        {
            _input.Press(_keyboard.escapeKey);
            yield return null;
            _input.Release(_keyboard.escapeKey);
            yield return null;
        }

        private IEnumerator ClickButton(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            Vector2 buttonCenter = (corners[0] + corners[2]) * 0.5f;

            _input.Move(_mouse.position, buttonCenter);
            yield return null;
            _input.Press(_mouse.leftButton);
            yield return null;
            _input.Release(_mouse.leftButton);
            yield return null;
        }

        private IEnumerator MovePointerToCenter(int width, int height)
        {
            InputSystem.Update();
            _input.Move(_mouse.position, new Vector2(width * 0.5f, height * 0.5f));
            yield return null;
        }

        private static IEnumerator ApplyScreenSize(int width, int height, SystemOverlay overlay)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
#if UNITY_EDITOR
            SetExistingEditorGameViewSize(width, height);
#endif
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();

            Rect pixelRect = overlay.OverlayCanvas.pixelRect;
            Assert.That(Mathf.RoundToInt(pixelRect.width), Is.EqualTo(width), "Canvas width mismatch");
            Assert.That(Mathf.RoundToInt(pixelRect.height), Is.EqualTo(height), "Canvas height mismatch");
        }

#if UNITY_EDITOR
        private static void SetExistingEditorGameViewSize(int width, int height)
        {
            Assembly editorAsm = typeof(EditorWindow).Assembly;
            Type gameViewType = editorAsm.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                return;
            }

            UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(gameViewType);
            if (windows == null || windows.Length == 0)
            {
                return;
            }

            var gameView = windows[0] as EditorWindow;
            Type sizesType = editorAsm.GetType("UnityEditor.GameViewSizes");
            Type gameViewSizeType = editorAsm.GetType("UnityEditor.GameViewSize");
            Type gameViewSizeEnum = editorAsm.GetType("UnityEditor.GameViewSizeType");
            if (gameView == null || sizesType == null || gameViewSizeType == null)
            {
                return;
            }

            object instance = sizesType.GetProperty(
                "instance",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            object currentGroup = sizesType.GetProperty("currentGroup")?.GetValue(instance);
            if (currentGroup == null)
            {
                return;
            }

            int idx = FindOrAddCustomSize(currentGroup, gameViewSizeType, gameViewSizeEnum, width, height);
            PropertyInfo selectedProp = gameViewType.GetProperty(
                "selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            selectedProp?.SetValue(gameView, idx);
            gameView.Repaint();
        }

        private static int FindOrAddCustomSize(
            object group,
            Type gameViewSizeType,
            Type gameViewSizeEnum,
            int width,
            int height)
        {
            MethodInfo getTotalCount = group.GetType().GetMethod("GetTotalCount");
            MethodInfo getGameViewSize = group.GetType().GetMethod("GetGameViewSize");
            int total = (int)getTotalCount.Invoke(group, null);
            for (int i = 0; i < total; i++)
            {
                object sz = getGameViewSize.Invoke(group, new object[] { i });
                int w = (int)sz.GetType().GetProperty("width").GetValue(sz);
                int h = (int)sz.GetType().GetProperty("height").GetValue(sz);
                if (w == width && h == height)
                {
                    return i;
                }
            }

            object fixedResEnum = Enum.Parse(gameViewSizeEnum, "FixedResolution");
            object customSize = Activator.CreateInstance(
                gameViewSizeType,
                fixedResEnum,
                width,
                height,
                $"Test {width}x{height}");
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new object[] { customSize });
            return (int)getTotalCount.Invoke(group, null) - 1;
        }
#endif

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

        private static void AssertPoseEqual(CameraPose actual, CameraPose expected, float tol)
        {
            Assert.That(actual.Cu, Is.EqualTo(expected.Cu).Within(tol));
            Assert.That(actual.Cv, Is.EqualTo(expected.Cv).Within(tol));
            Assert.That(actual.H, Is.EqualTo(expected.H).Within(tol));
        }
    }
}
