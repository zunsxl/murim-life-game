using Murim.Core.Exploration.Session;
using Murim.View.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Reads Input System Keyboard.current and Mouse.current directly for CE-1 through CE-15 per §3.8.
    /// </summary>
    public sealed class ExplorationInput
    {
        private Vector2 _prevDragPos;

        public void ProcessFrame(
            ExplorationSession session,
            MenuController menuController,
            CameraRig cameraRig,
            float clampedDt)
        {
            if (session == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ToggleMenu(session, menuController);
            }

            float aspect = cameraRig != null ? cameraRig.CurrentAspect : CameraModel.DefaultAspect;
            PanInput pan = ReadPanInput(keyboard);
            session.PanKeys(pan, clampedDt, aspect);

            if (!session.CanSimulate)
            {
                return;
            }

            ProcessActionKeys(session, keyboard, aspect);
            ProcessMouse(session, mouse, cameraRig, aspect);
        }

        private static void ToggleMenu(ExplorationSession session, MenuController menuController)
        {
            if (menuController != null)
            {
                menuController.Toggle();
                return;
            }

            if (session.Paused)
            {
                session.Resume();
            }
            else
            {
                session.OpenMenu();
            }
        }

        private static PanInput ReadPanInput(Keyboard keyboard)
        {
            if (keyboard == null)
            {
                return default;
            }

            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            bool up = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed;
            bool down = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed;
            return new PanInput(left, right, up, down);
        }

        private static void ProcessActionKeys(ExplorationSession session, Keyboard keyboard, float aspect)
        {
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.pageUpKey.wasPressedThisFrame || keyboard.rightBracketKey.wasPressedThisFrame)
            {
                session.StepFloorUp();
            }

            if (keyboard.pageDownKey.wasPressedThisFrame || keyboard.leftBracketKey.wasPressedThisFrame)
            {
                session.StepFloorDown();
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                session.CenterOnPlaceholder(aspect);
            }

            if (keyboard.homeKey.wasPressedThisFrame)
            {
                session.ResetView(aspect);
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                session.Ascend();
            }

            if (keyboard.fKey.wasPressedThisFrame)
            {
                session.Descend();
            }
        }

        private void ProcessMouse(
            ExplorationSession session,
            Mouse mouse,
            CameraRig cameraRig,
            float aspect)
        {
            if (mouse == null)
            {
                return;
            }

            ResolveScreenSize(cameraRig, out float widthPx, out float heightPx);
            ProcessDrag(session, mouse, heightPx, aspect);
            ProcessScroll(session, mouse, aspect);
            ProcessLeftClick(session, mouse, widthPx, heightPx, aspect);
        }

        private void ProcessDrag(
            ExplorationSession session,
            Mouse mouse,
            float screenHeightPx,
            float aspect)
        {
            bool dragStart = mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            bool dragHeld = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            Vector2 mousePos = mouse.position.ReadValue();

            if (dragStart && !session.Camera.IsDragging)
            {
                session.BeginDrag();
                _prevDragPos = mousePos;
            }

            if (!session.Camera.IsDragging)
            {
                return;
            }

            if (!dragHeld)
            {
                session.EndDrag();
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            if (delta.x == 0f && delta.y == 0f)
            {
                delta = mousePos - _prevDragPos;
            }
            _prevDragPos = mousePos;

            if (delta.x != 0f || delta.y != 0f)
            {
                session.Drag(delta.x, delta.y, screenHeightPx, aspect);
            }
        }

        private static void ProcessScroll(ExplorationSession session, Mouse mouse, float aspect)
        {
            float scrollY = mouse.scroll.ReadValue().y;
            if (scrollY > 0f)
            {
                session.ZoomNotch(1, aspect);
            }
            else if (scrollY < 0f)
            {
                session.ZoomNotch(-1, aspect);
            }
        }

        private static void ProcessLeftClick(
            ExplorationSession session,
            Mouse mouse,
            float widthPx,
            float heightPx,
            float aspect)
        {
            if (!mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Vector2 pos = mouse.position.ReadValue();
            float vx = widthPx > 0f ? pos.x / widthPx : 0.5f;
            float vy = heightPx > 0f ? pos.y / heightPx : 0.5f;
            if (vx >= 0f && vx <= 1f && vy >= 0f && vy <= 1f)
            {
                session.ClickViewport(vx, vy, aspect);
            }
        }

        private static void ResolveScreenSize(CameraRig cameraRig, out float widthPx, out float heightPx)
        {
            if (cameraRig != null && cameraRig.Camera != null && cameraRig.Camera.pixelWidth > 0 && cameraRig.Camera.pixelHeight > 0)
            {
                widthPx = cameraRig.Camera.pixelWidth;
                heightPx = cameraRig.Camera.pixelHeight;
                return;
            }

            widthPx = Screen.width > 0 ? Screen.width : 1920f;
            heightPx = Screen.height > 0 ? Screen.height : 1080f;
        }
    }
}
