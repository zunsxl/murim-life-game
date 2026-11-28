#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Murim.Core.Exploration.Placeholder;
using Murim.Core.Exploration.Session;
using Murim.Core.Valley;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Murim.View.Exploration
{
    /// <summary>
    /// F3 navigation overlay for Editor and Development builds only per §3.10. Mutates zero simulation state.
    /// </summary>
    public sealed class DevOverlay : MonoBehaviour
    {
        private const float CellTintOffset = 0.04f;
        private const float BoundsOutlineHeight = 17.04f;
        private const float BoundsOutlineThickness = 0.18f;

        private static readonly Color NonWalkableTintColor = new Color(0.95f, 0.22f, 0.2f, 0.30f);
        private static readonly Color AirBoundsColor = new Color(0.2f, 0.85f, 0.95f, 0.85f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly StringBuilder _sb = new StringBuilder(192);
        private ValleyLayout _layout;
        private ExplorationSession _session;
        private GameObject _visualRoot;
        private Mesh _nonWalkableMesh;
        private Mesh _airBoundsMesh;
        private Material _nonWalkableMat;
        private Material _airBoundsMat;
        private float _lastDt;
        private string _cachedHudText = string.Empty;
        private GUIStyle _labelStyle;
        private bool _visible;
        private bool _builtVisuals;

        public bool IsVisible => _visible;

        private void OnDestroy()
        {
            PlaceholderView.DestroyOwnedAsset(_nonWalkableMesh);
            PlaceholderView.DestroyOwnedAsset(_airBoundsMesh);
            PlaceholderView.DestroyOwnedAsset(_nonWalkableMat);
            PlaceholderView.DestroyOwnedAsset(_airBoundsMat);
        }

        public void Initialize(ValleyLayout layout, ExplorationSession session)
        {
            _layout = layout;
            _session = session;
            _visible = false;
        }

        public void UpdateOverlay(ExplorationSession session, ValleyLayout layout, float unscaledDeltaTime)
        {
            _session = session;
            _layout = layout;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
            {
                Toggle();
            }

            if (!_visible || _session == null || _layout == null)
            {
                return;
            }

            _lastDt = unscaledDeltaTime;
            FormatReadout();
        }

        public void Toggle()
        {
            _visible = !_visible;
            if (_visible && !_builtVisuals)
            {
                BuildVisuals();
            }

            if (_visualRoot != null)
            {
                _visualRoot.SetActive(_visible);
            }
        }

        private void OnGUI()
        {
            if (!_visible || _session == null)
            {
                return;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 14
                };
                _labelStyle.normal.textColor = Color.white;
            }

            GUI.Box(new Rect(12f, 12f, 320f, 110f), _cachedHudText, _labelStyle);
        }

        private void FormatReadout()
        {
            PlaceholderState state = _session.Placeholder;
            int cx = Mathf.Clamp(Mathf.FloorToInt(state.Position.X), 0, _layout.Width - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt(state.Position.Y), 0, _layout.Depth - 1);

            _sb.Clear();
            _sb.Append("Cell: (").Append(cx).Append(", ").Append(cz).Append(")\n");
            _sb.Append("Elevation: ").Append(state.Elevation.ToString("F2", CultureInfo.InvariantCulture)).Append(" m\n");
            _sb.Append("Layer: ");
            if (state.AirLayer == 0)
            {
                _sb.Append("On Foot\n");
            }
            else
            {
                _sb.Append("Air Layer ").Append(state.AirLayer).Append('\n');
            }

            _sb.Append("Viewed Floor: ");
            int viewed = _session.FloorView.ViewedFloor;
            if (viewed == 0)
            {
                _sb.Append("Full View\n");
            }
            else
            {
                _sb.Append("Ground Floor ").Append(viewed).Append('\n');
            }

            float frameMs = _lastDt * 1000f;
            _sb.Append("Frame: ").Append(frameMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms");
            _cachedHudText = _sb.ToString();
        }

        private void BuildVisuals()
        {
            if (_layout == null)
            {
                return;
            }

            _builtVisuals = true;
            _visualRoot = new GameObject("DevOverlayVisuals");
            _visualRoot.layer = Layers.WorldLayer;
            _visualRoot.transform.SetParent(transform, false);

            _nonWalkableMesh = BuildNonWalkableMesh(_layout);
            _nonWalkableMat = PlaceholderView.CreateOverlayUnlitMaterial(null);
            _nonWalkableMat.SetColor(BaseColorId, NonWalkableTintColor);
            CreateOverlayChild("NonWalkableTint", _nonWalkableMesh, _nonWalkableMat);

            _airBoundsMesh = BuildAirBoundsMesh(_layout.AirBounds);
            _airBoundsMat = PlaceholderView.CreateOverlayUnlitMaterial(null);
            _airBoundsMat.SetColor(BaseColorId, AirBoundsColor);
            CreateOverlayChild("AirBoundsOutline", _airBoundsMesh, _airBoundsMat);
        }

        private void CreateOverlayChild(string childName, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(childName);
            go.layer = Layers.WorldLayer;
            go.transform.SetParent(_visualRoot.transform, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static Mesh BuildNonWalkableMesh(ValleyLayout layout)
        {
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            for (int cz = 0; cz < layout.Depth; cz++)
            {
                for (int cx = 0; cx < layout.Width; cx++)
                {
                    if (layout.IsWalkable(cx, cz))
                    {
                        continue;
                    }

                    int b = verts.Count;
                    verts.Add(new Vector3(cx, layout.VertexHeight(cx, cz) + CellTintOffset, cz));
                    verts.Add(new Vector3(cx + 1f, layout.VertexHeight(cx + 1, cz) + CellTintOffset, cz));
                    verts.Add(new Vector3(cx + 1f, layout.VertexHeight(cx + 1, cz + 1) + CellTintOffset, cz + 1f));
                    verts.Add(new Vector3(cx, layout.VertexHeight(cx, cz + 1) + CellTintOffset, cz + 1f));
                    tris.Add(b + 0);
                    tris.Add(b + 2);
                    tris.Add(b + 1);
                    tris.Add(b + 0);
                    tris.Add(b + 3);
                    tris.Add(b + 2);
                }
            }

            Mesh mesh = new Mesh { name = "DevOverlayNonWalkable" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildAirBoundsMesh(AirBounds bounds)
        {
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            float t = BoundsOutlineThickness;
            AppendRectXZ(verts, tris, bounds.MinX, bounds.MaxX, bounds.MinZ, bounds.MinZ + t);
            AppendRectXZ(verts, tris, bounds.MinX, bounds.MaxX, bounds.MaxZ - t, bounds.MaxZ);
            AppendRectXZ(verts, tris, bounds.MinX, bounds.MinX + t, bounds.MinZ, bounds.MaxZ);
            AppendRectXZ(verts, tris, bounds.MaxX - t, bounds.MaxX, bounds.MinZ, bounds.MaxZ);

            Mesh mesh = new Mesh { name = "DevOverlayAirBounds" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendRectXZ(
            List<Vector3> verts,
            List<int> tris,
            float x0,
            float x1,
            float z0,
            float z1)
        {
            int b = verts.Count;
            verts.Add(new Vector3(x0, BoundsOutlineHeight, z0));
            verts.Add(new Vector3(x1, BoundsOutlineHeight, z0));
            verts.Add(new Vector3(x1, BoundsOutlineHeight, z1));
            verts.Add(new Vector3(x0, BoundsOutlineHeight, z1));
            tris.Add(b + 0);
            tris.Add(b + 2);
            tris.Add(b + 1);
            tris.Add(b + 0);
            tris.Add(b + 3);
            tris.Add(b + 2);
        }
    }
}
#endif
