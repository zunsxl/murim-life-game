using System;
using System.Collections;
using System.IO;
using Murim.Core.Exploration.Placeholder;
using Murim.Core.Exploration.Session;
using Murim.Core.Valley;
using Murim.View;
using Murim.View.Exploration;
using Murim.View.UI;
using Murim.View.World;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Murim.Tests.Engine.Exploration
{
    [TestFixture]
    public sealed class ExplorationViewTests
    {
        private const string ValleyScenePath = "Assets/Scenes/Valley.unity";

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private Scene _previousScene;
        private Scene _loadedValleyScene;
        private bool _hasLoadedValley;
        private ExplorationBootstrap _bootstrap;

        [SetUp]
        public void SetUp()
        {
            _previousScene = SceneManager.GetActiveScene();
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_hasLoadedValley && _loadedValleyScene.IsValid() && _loadedValleyScene.isLoaded)
            {
                if (_previousScene.IsValid() && _previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(_previousScene);
                }

                AsyncOperation unload = SceneManager.UnloadSceneAsync(_loadedValleyScene);
                while (unload != null && !unload.isDone)
                {
                    yield return null;
                }
                _hasLoadedValley = false;
            }

            _bootstrap = null;
            _input?.TearDown();
            _input = null;
            _keyboard = null;
            _mouse = null;
        }

        // T-ENG-10: Load Valley.unity and wait 10 frames -> fixed camera rotation, Full View, capsule at start & visible, label/marker/menu inactive, no AudioSource.
        [UnityTest]
        public IEnumerator T_ENG_10_ValleyLaunchesInFullViewWithCapsuleVisibleAndUiInactive()
        {
            yield return LoadValleyAndWaitFrames(10);
            ExplorationBootstrap bootstrap = _bootstrap;

            Camera cam = bootstrap.CameraRig.Camera;
            Assert.That(Quaternion.Angle(cam.transform.rotation, Quaternion.Euler(45f, 0f, 0f)), Is.LessThan(1e-3f));
            Assert.That(cam.orthographic, Is.True);
            Assert.That(cam.nearClipPlane, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(cam.farClipPlane, Is.EqualTo(300f).Within(1e-4f));
            Assert.That(cam.cullingMask, Is.EqualTo((1 << Layers.WorldLayer) | (1 << Layers.OverlayLayer)));

            Assert.That(bootstrap.Session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(bootstrap.PlaceholderView.CapsuleVisible, Is.True);
            Assert.That(bootstrap.PlaceholderView.CueVisible, Is.False);
            Assert.That(bootstrap.PlaceholderView.transform.position.x, Is.EqualTo(70.5f).Within(0.01f));
            Assert.That(bootstrap.PlaceholderView.transform.position.z, Is.EqualTo(28.5f).Within(0.01f));

            Assert.That(bootstrap.FloorLabelView.IsActive, Is.False);
            Assert.That(bootstrap.FloorLabelView.gameObject.activeInHierarchy, Is.False);
            Assert.That(bootstrap.DestinationMarkerView.IsActive, Is.False);
            Assert.That(bootstrap.DestinationMarkerView.MarkerObject.activeInHierarchy, Is.False);
            Assert.That(bootstrap.MenuController.IsOpen, Is.False);

            Component[] components = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    string typeName = components[i].GetType().Name;
                    Assert.That(typeName, Is.Not.EqualTo("AudioSource"));
                    Assert.That(typeName, Is.Not.EqualTo("AudioListener"));
                }
            }
        }

        // T-ENG-11: Simulate CE-1 through CE-10 bindings; verify state changes and constant camera rotation.
        [UnityTest]
        public IEnumerator T_ENG_11_ControlsCe1ToCe10UpdateSessionAndKeepCameraRotationFixed()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            ExplorationSession session = bootstrap.Session;
            Camera cam = bootstrap.CameraRig.Camera;
            Quaternion expectedRot = Quaternion.Euler(45f, 0f, 0f);

            // CE-1: WASD and Arrow keys
            Vec2 startFocus = session.Camera.Focus;
            _input.Press(_keyboard.dKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.dKey);
            yield return null;
            Assert.That(session.Camera.Focus.X, Is.GreaterThan(startFocus.X));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            float afterD = session.Camera.Focus.X;
            _input.Press(_keyboard.aKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.aKey);
            yield return null;
            Assert.That(session.Camera.Focus.X, Is.LessThan(afterD));

            float afterA = session.Camera.Focus.X;
            _input.Press(_keyboard.rightArrowKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.rightArrowKey);
            yield return null;
            Assert.That(session.Camera.Focus.X, Is.GreaterThan(afterA));

            float afterRightArrow = session.Camera.Focus.X;
            _input.Press(_keyboard.leftArrowKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.leftArrowKey);
            yield return null;
            Assert.That(session.Camera.Focus.X, Is.LessThan(afterRightArrow));

            float beforeW = session.Camera.Focus.Y;
            _input.Press(_keyboard.wKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.wKey);
            yield return null;
            Assert.That(session.Camera.Focus.Y, Is.GreaterThan(beforeW));

            float afterW = session.Camera.Focus.Y;
            _input.Press(_keyboard.sKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.sKey);
            yield return null;
            Assert.That(session.Camera.Focus.Y, Is.LessThan(afterW));

            float afterS = session.Camera.Focus.Y;
            _input.Press(_keyboard.upArrowKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.upArrowKey);
            yield return null;
            Assert.That(session.Camera.Focus.Y, Is.GreaterThan(afterS));

            float afterUpArrow = session.Camera.Focus.Y;
            _input.Press(_keyboard.downArrowKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.downArrowKey);
            yield return null;
            Assert.That(session.Camera.Focus.Y, Is.LessThan(afterUpArrow));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-2: Right-mouse drag and middle-mouse drag
            Vector2 center = GetViewportPixelCenter(cam);
            _input.Set(_mouse.position, center);
            yield return null;
            _input.Press(_mouse.rightButton);
            yield return null;
            _input.Set(_mouse.position, center + new Vector2(80f, 0f));
            yield return null;
            _input.Release(_mouse.rightButton);
            yield return null;
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            float beforeMid = session.Camera.Focus.Y;
            _input.Set(_mouse.position, center);
            yield return null;
            _input.Press(_mouse.middleButton);
            yield return null;
            _input.Set(_mouse.position, center + new Vector2(0f, 60f));
            yield return null;
            _input.Release(_mouse.middleButton);
            yield return null;
            Assert.That(session.Camera.Focus.Y, Is.LessThan(beforeMid));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-3: Scroll wheel zoom
            float orthoBefore = session.Camera.OrthoSize;
            _input.Set(_mouse.scroll, new Vector2(0f, 120f));
            yield return null;
            _input.Set(_mouse.scroll, Vector2.zero);
            yield return null;
            Assert.That(session.Camera.OrthoSize, Is.LessThan(orthoBefore));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-5 / CE-4: PageDown / [ and PageUp / ]
            yield return TapKey(_keyboard.leftBracketKey);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(4));
            yield return TapKey(_keyboard.pageDownKey);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(3));
            yield return TapKey(_keyboard.rightBracketKey);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(4));
            yield return TapKey(_keyboard.pageUpKey);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-6: Space centers on placeholder and restores visibility
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.leftBracketKey); // Floor 1
            yield return TapKey(_keyboard.spaceKey);
            Assert.That(session.Camera.Focus.X, Is.EqualTo(session.Placeholder.Position.X).Within(0.05f));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-7: Home resets view
            yield return TapKey(_keyboard.homeKey);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(session.Camera.OrthoSize, Is.EqualTo(session.Tuning.DefaultOrthoSize).Within(1e-3f));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-8: Left-click on valley sends placeholder moving
            Vector2 clickPx = new Vector2(center.x + 120f, center.y);
            _input.Set(_mouse.position, clickPx);
            yield return null;
            _input.Press(_mouse.leftButton);
            yield return null;
            _input.Release(_mouse.leftButton);
            for (int i = 0; i < 120 && session.Placeholder.Position.X <= 70.5f; i++)
            {
                yield return null;
            }
            Assert.That(session.MarkerVisible, Is.True);
            Assert.That(session.Placeholder.Position.X, Is.GreaterThan(70.5f));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            // CE-9 / CE-10: R ascends, F descends
            yield return TapKey(_keyboard.rKey);
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(1));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));

            yield return TapKey(_keyboard.fKey);
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(0));
            Assert.That(Quaternion.Angle(cam.transform.rotation, expectedRot), Is.LessThan(1e-3f));
        }

        // T-ENG-12: Menu buttons, Esc toggle, no world click-through, QuitRequested once, Enter/Space do not trigger buttons.
        [UnityTest]
        public IEnumerator T_ENG_12_SystemMenuBlocksWorldInputPreventsClickThroughAndIgnoresKeyboardSubmit()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            ExplorationSession session = bootstrap.Session;
            MenuController menu = bootstrap.MenuController;

            int quitCount = 0;
            menu.QuitRequested = () => quitCount++;

            // Open menu via Esc
            yield return TapKey(_keyboard.escapeKey);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(session.Paused, Is.True);

            // Verify exact button labels
            Assert.That(menu.ResumeButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Resume"));
            Assert.That(menu.ResetViewButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Reset View"));
            Assert.That(menu.QuitButton.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Quit"));

            // World inputs while open have no effect
            Vec2 focusBefore = session.Camera.Focus;
            yield return TapKey(_keyboard.rKey);
            yield return TapKey(_keyboard.pageDownKey);
            _input.Press(_keyboard.dKey);
            yield return StepFrames(3);
            _input.Release(_keyboard.dKey);
            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(0));
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(session.Camera.Focus, Is.EqualTo(focusBefore));

            // Click Resume -> closes without creating destination marker
            yield return ClickUiButton(menu.ResumeButton);
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(session.Paused, Is.False);
            Assert.That(session.MarkerVisible, Is.False);

            // Pan away & slice, then open menu and click Reset View
            yield return TapKey(_keyboard.leftBracketKey);
            _input.Press(_keyboard.dKey);
            yield return StepFrames(4);
            _input.Release(_keyboard.dKey);
            yield return TapKey(_keyboard.escapeKey);
            Assert.That(menu.IsOpen, Is.True);

            yield return ClickUiButton(menu.ResetViewButton);
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(0));
            Assert.That(session.Camera.Focus.X, Is.EqualTo(session.Tuning.DefaultFocusX).Within(1e-3f));
            Assert.That(session.MarkerVisible, Is.False);

            // Open menu, press Enter and Space -> no button triggered, menu stays open
            yield return TapKey(_keyboard.escapeKey);
            Assert.That(menu.IsOpen, Is.True);
            yield return TapKey(_keyboard.enterKey);
            yield return TapKey(_keyboard.spaceKey);
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(quitCount, Is.EqualTo(0));

            // Close via Esc, reopen via Esc, click Quit -> QuitRequested invoked once
            yield return TapKey(_keyboard.escapeKey);
            Assert.That(menu.IsOpen, Is.False);
            yield return TapKey(_keyboard.escapeKey);
            yield return ClickUiButton(menu.QuitButton);
            Assert.That(quitCount, Is.EqualTo(1));
        }

        // T-ENG-13: 5 label events in 5 frames -> one label object, text of last event, fades to alpha 0 and deactivates.
        [UnityTest]
        public IEnumerator T_ENG_13_RapidLabelEventsReuseSingleLabelAndFadeToInactive()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            FloorLabelView label = bootstrap.FloorLabelView;
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.leftBracketKey);
            yield return TapKey(_keyboard.rKey);

            FloorLabelView[] allLabels = UnityEngine.Object.FindObjectsByType<FloorLabelView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(allLabels.Length, Is.EqualTo(1));
            Assert.That(label.IsActive, Is.True);
            Assert.That(label.CurrentText, Is.EqualTo("Air Layer 1"));
            Assert.That(label.CurrentAlpha, Is.EqualTo(1f).Within(1e-3f));

            // Advance until faded out (default 1.5 + 0.5 = 2.0 s)
            float dt = 0.05f;
            for (int i = 0; i < 60 && label.IsActive; i++)
            {
                bootstrap.FloorLabelView.UpdateView(bootstrap.Session, dt);
                yield return null;
            }

            Assert.That(label.IsActive, Is.False);
            Assert.That(label.CurrentAlpha, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(label.gameObject.activeSelf, Is.False);
        }

        // T-ENG-14: Click 5 times in 5 frames (reachable & unreachable) -> one marker, last texture, then fades and deactivates.
        [UnityTest]
        public IEnumerator T_ENG_14_RapidClicksReuseSingleMarkerWithLastTextureAndFadeOut()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            DestinationMarkerView markerView = bootstrap.DestinationMarkerView;
            Texture reachableTex = bootstrap.WorldView.Catalog.GetTexture(ArtAssetId.MarkerReachable);
            Texture unreachableTex = bootstrap.WorldView.Catalog.GetTexture(ArtAssetId.MarkerUnreachable);

            // 4 reachable clicks + final unreachable click (onto river south of bank)
            Camera cam = bootstrap.CameraRig.Camera;
            Vector2 center = GetViewportPixelCenter(cam);
            for (int i = 0; i < 4; i++)
            {
                _input.Set(_mouse.position, center + new Vector2(20f * i, 0f));
                yield return null;
                _input.Press(_mouse.leftButton);
                yield return null;
                _input.Release(_mouse.leftButton);
                yield return null;
            }

            // Final unreachable click onto the river channel (z = 5 m)
            bootstrap.Session.Click(
                new Vec3(70.5f, 10f, -5f),
                new Vec3(0f, -ValleyConstants.Sin45, ValleyConstants.Cos45));
            bootstrap.UpdateViews(0.016f);

            DestinationMarkerView[] allMarkers = UnityEngine.Object.FindObjectsByType<DestinationMarkerView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(allMarkers.Length, Is.EqualTo(1));
            Assert.That(markerView.IsActive, Is.True);
            Assert.That(bootstrap.Session.Marker.Kind, Is.EqualTo(MarkerKind.Unreachable));
            Assert.That(markerView.CurrentTexture, Is.EqualTo(unreachableTex));
            Assert.That(reachableTex, Is.Not.EqualTo(unreachableTex));

            for (int i = 0; i < 40 && markerView.IsActive; i++)
            {
                markerView.UpdateView(bootstrap.Session, 0.05f);
                yield return null;
            }

            Assert.That(markerView.IsActive, Is.False);
            Assert.That(markerView.MarkerObject.activeSelf, Is.False);
        }

        // T-ENG-15: Ascend, step view below placeholder, then Space -> cue visible at SurfaceAt, capsule+cue hide when sliced, return on Space.
        [UnityTest]
        public IEnumerator T_ENG_15_AirborneCueFollowsSurfaceAtAndHidesWithCapsuleWhenSliced()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            PlaceholderView pv = bootstrap.PlaceholderView;
            ExplorationSession session = bootstrap.Session;

            yield return TapKey(_keyboard.rKey);
            for (int i = 0; i < 50; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
            }

            Assert.That(session.Placeholder.AirLayer, Is.EqualTo(1));
            Assert.That(pv.CapsuleVisible, Is.True);
            Assert.That(pv.CueVisible, Is.True);
            float expectedSurface = session.Layout.SurfaceAt(session.Placeholder.Position.X, session.Placeholder.Position.Y);
            Assert.That(pv.CueRingPosition.y, Is.EqualTo(expectedSurface).Within(1e-4f));

            // Step down to Ground Floor 1 (below band 2 of Air Layer 1) and advance fade
            yield return TapKey(_keyboard.leftBracketKey); // 4
            yield return TapKey(_keyboard.leftBracketKey); // 3
            yield return TapKey(_keyboard.leftBracketKey); // 2
            yield return TapKey(_keyboard.leftBracketKey); // 1
            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(1));

            for (int i = 0; i < 30; i++)
            {
                pv.UpdateView(
                    session,
                    session.Layout,
                    session.Placeholder.Position,
                    session.Placeholder.Position,
                    session.Placeholder.Elevation,
                    session.Placeholder.Elevation,
                    1f,
                    0.05f);
            }

            Assert.That(pv.CapsuleVisible, Is.False);
            Assert.That(pv.CueVisible, Is.False);

            // Space restores both capsule and cue visibility
            yield return TapKey(_keyboard.spaceKey);
            for (int i = 0; i < 30; i++)
            {
                pv.UpdateView(
                    session,
                    session.Layout,
                    session.Placeholder.Position,
                    session.Placeholder.Position,
                    session.Placeholder.Elevation,
                    session.Placeholder.Elevation,
                    1f,
                    0.05f);
            }

            Assert.That(session.FloorView.ViewedFloor, Is.EqualTo(2));
            Assert.That(pv.CapsuleVisible, Is.True);
            Assert.That(pv.CueVisible, Is.True);
        }

        // T-ENG-18: Resize game view to 1280x720, 2560x1080, 1600x1200 with menu open -> panel and buttons inside screen, no text truncated.
        [UnityTest]
        public IEnumerator T_ENG_18_MenuFitsInsideScreenWithoutTextTruncationAcrossAspectRatios()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            bootstrap.MenuController.Open();
            yield return null;

            Canvas canvas = bootstrap.UiCanvas;
            MenuController menu = bootstrap.MenuController;
            RectTransform panelRt = menu.PanelRectTransform;
            Button[] buttons = { menu.ResumeButton, menu.ResetViewButton, menu.QuitButton };

            Vector2Int[] resolutions =
            {
                new Vector2Int(1280, 720),
                new Vector2Int(2560, 1080),
                new Vector2Int(1600, 1200)
            };

            Vector3[] corners = new Vector3[4];
            for (int r = 0; r < resolutions.Length; r++)
            {
                int w = resolutions[r].x;
                int h = resolutions[r].y;
#if UNITY_EDITOR
                UnityEditor.PlayModeWindow.SetCustomRenderingResolution((uint)w, (uint)h, $"{w}x{h}");
#else
                Screen.SetResolution(w, h, false);
#endif
                yield return null;
                Canvas.ForceUpdateCanvases();
                yield return null;

                Rect canvasRect = canvas.pixelRect;
                AssertRectInside(panelRt, canvasRect, corners, $"Panel at {w}x{h}");

                for (int b = 0; b < buttons.Length; b++)
                {
                    RectTransform btnRt = buttons[b].GetComponent<RectTransform>();
                    AssertRectInside(btnRt, canvasRect, corners, $"Button {b} at {w}x{h}");

                    TMP_Text tmp = buttons[b].GetComponentInChildren<TMP_Text>();
                    tmp.ForceMeshUpdate();
                    Assert.That(tmp.isTextTruncated, Is.False, $"Button {b} text truncated at {w}x{h}");
                    Assert.That(tmp.preferredWidth, Is.LessThanOrEqualTo(btnRt.rect.width + 0.5f));
                }
            }
        }

        // T-ENG-19: Focus false mid-walk and mid-ascent -> holds position, walk stays cancelled on refocus, ascent finishes, _MurimAmbientTime advances.
        [UnityTest]
        public IEnumerator T_ENG_19_FocusLossCancelsWalkHoldsAndFinishesAscentWhileAmbientAdvances()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            ExplorationSession session = bootstrap.Session;

            // 1. Mid-walk focus loss
            session.ClickViewport(0.8f, 0.5f, bootstrap.CameraRig.CurrentAspect);
            for (int i = 0; i < 10; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
            }

            Vec2 posBeforeUnfocus = session.Placeholder.Position;
            float ambientBefore = Shader.GetGlobalFloat("_MurimAmbientTime");
            bootstrap.HandleApplicationFocus(false);

            for (int i = 0; i < 30; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
                yield return null;
            }

            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforeUnfocus));
            float ambientDuringUnfocus = Shader.GetGlobalFloat("_MurimAmbientTime");
            Assert.That(ambientDuringUnfocus, Is.GreaterThan(ambientBefore));

            bootstrap.HandleApplicationFocus(true);
            for (int i = 0; i < 20; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
            }
            Assert.That(session.Placeholder.Position, Is.EqualTo(posBeforeUnfocus));
            Assert.That(session.Placeholder.HasDestination, Is.False);

            // 2. Mid-ascent focus loss
            session.Ascend();
            for (int i = 0; i < 5; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
            }

            float elevBeforeUnfocus = session.Placeholder.Elevation;
            Assert.That(elevBeforeUnfocus, Is.GreaterThan(0f));
            Assert.That(elevBeforeUnfocus, Is.LessThan(ValleyConstants.AirLayer1Altitude));

            bootstrap.HandleApplicationFocus(false);
            for (int i = 0; i < 20; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
            }
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(elevBeforeUnfocus));

            bootstrap.HandleApplicationFocus(true);
            for (int i = 0; i < 60; i++)
            {
                bootstrap.StepSimulation(ValleyConstants.TickSeconds);
                bootstrap.UpdateViews(ValleyConstants.TickSeconds);
            }
            Assert.That(session.Placeholder.Elevation, Is.EqualTo(ValleyConstants.AirLayer1Altitude).Within(1e-4f));
        }

        // T-ENG-20: 600 frames while panning and walking -> 0 bytes allocated around session tick and view update calls after frame 60.
        [UnityTest]
        public IEnumerator T_ENG_20_ZeroPerFrameGcAllocationsAfterWarmupWhilePanningAndWalking()
        {
            yield return LoadValleyAndWaitFrames(3);
            ExplorationBootstrap bootstrap = _bootstrap;

            // Disable automatic Update so this test drives one deterministic step per rendered frame.
            bootstrap.enabled = false;
            float aspect = bootstrap.CameraRig.CurrentAspect;

            _input.Press(_keyboard.dKey);
            bootstrap.Session.ClickViewport(0.85f, 0.55f, aspect);

            long allocatedAfterWarmup = 0;
            for (int frame = 0; frame < 600; frame++)
            {
                if (frame % 120 == 60)
                {
                    bootstrap.Session.ClickViewport(0.15f, 0.5f, aspect);
                }
                else if (frame % 120 == 0 && frame > 0)
                {
                    bootstrap.Session.ClickViewport(0.85f, 0.55f, aspect);
                }

                if (frame >= 60)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    bootstrap.StepFrame(ValleyConstants.TickSeconds);
                    long after = GC.GetAllocatedBytesForCurrentThread();
                    allocatedAfterWarmup += (after - before);
                }
                else
                {
                    bootstrap.StepFrame(ValleyConstants.TickSeconds);
                }

                yield return null;
            }

            _input.Release(_keyboard.dKey);
            Assert.That(allocatedAfterWarmup, Is.EqualTo(0));
        }

        // T-TOOL-03: Static check that the entire DevOverlay.cs file is wrapped inside #if UNITY_EDITOR || DEVELOPMENT_BUILD.
        [Test]
        public void T_TOOL_03_DevOverlayIsFullyWrappedInEditorOrDevelopmentBuildGuard()
        {
            string path = Path.Combine(Application.dataPath, "Scripts", "View", "Exploration", "DevOverlay.cs");
            Assert.That(File.Exists(path), Is.True, $"Missing {path}");

            string[] lines = File.ReadAllLines(path);
            int firstNonEmpty = -1;
            int lastNonEmpty = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    if (firstNonEmpty < 0)
                    {
                        firstNonEmpty = i;
                    }
                    lastNonEmpty = i;
                }
            }

            Assert.That(firstNonEmpty, Is.GreaterThanOrEqualTo(0));
            Assert.That(lines[firstNonEmpty].Trim(), Is.EqualTo("#if UNITY_EDITOR || DEVELOPMENT_BUILD"));
            Assert.That(lines[lastNonEmpty].Trim(), Is.EqualTo("#endif"));

            int depth = 0;
            for (int i = firstNonEmpty; i <= lastNonEmpty; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    depth++;
                }
                else if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    depth--;
                    if (i < lastNonEmpty)
                    {
                        Assert.That(depth, Is.GreaterThan(0), "DevOverlay guard closed before end of file.");
                    }
                }
            }
            Assert.That(depth, Is.EqualTo(0));
        }

        private IEnumerator LoadValleyAndWaitFrames(int frames)
        {
            AsyncOperation loadOp = SceneManager.LoadSceneAsync(ValleyScenePath, LoadSceneMode.Additive);
            while (!loadOp.isDone)
            {
                yield return null;
            }

            _loadedValleyScene = SceneManager.GetSceneByPath(ValleyScenePath);
            _hasLoadedValley = true;
            SceneManager.SetActiveScene(_loadedValleyScene);

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<ExplorationBootstrap>();
            Assert.That(_bootstrap, Is.Not.Null);
        }

        private IEnumerator TapKey(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            _input.Press(key);
            yield return null;
            _input.Release(key);
            yield return null;
        }

        private IEnumerator StepFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private IEnumerator ClickUiButton(Button button)
        {
            RectTransform rt = button.GetComponent<RectTransform>();
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, rt.position);
            _input.Set(_mouse.position, screenPos);
            yield return null;
            _input.Press(_mouse.leftButton);
            yield return null;
            _input.Release(_mouse.leftButton);
            yield return null;
        }

        private static Vector2 GetViewportPixelCenter(Camera cam)
        {
            return new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        }

        private static void AssertRectInside(RectTransform rt, Rect canvasRect, Vector3[] corners, string label)
        {
            rt.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(corners[i].x, Is.GreaterThanOrEqualTo(canvasRect.xMin - 0.5f), $"{label} left clipped");
                Assert.That(corners[i].x, Is.LessThanOrEqualTo(canvasRect.xMax + 0.5f), $"{label} right clipped");
                Assert.That(corners[i].y, Is.GreaterThanOrEqualTo(canvasRect.yMin - 0.5f), $"{label} bottom clipped");
                Assert.That(corners[i].y, Is.LessThanOrEqualTo(canvasRect.yMax + 0.5f), $"{label} top clipped");
            }
        }
    }
}
