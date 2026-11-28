using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Murim.View.Exploration
{
    /// <summary>
    /// Reusable draw descriptor for an OverlayLayer quad rendered with depth testing disabled.
    /// </summary>
    public sealed class OverlaySlot
    {
        public bool Active;
        public Mesh Mesh;
        public Matrix4x4 Matrix;
        public Material Material;
        public MaterialPropertyBlock PropertyBlock;
    }

    /// <summary>
    /// URP RenderGraph pass that renders OverlayLayer geometry after transparents with depth test disabled.
    /// </summary>
    public sealed class OverlayRenderPass : ScriptableRenderPass
    {
        private static readonly List<OverlaySlot> Slots = new List<OverlaySlot>(8);
        private static readonly BaseRenderFunc<OverlayPassData, RasterGraphContext> RenderFunc = ExecutePass;

        private sealed class OverlayPassData
        {
            internal List<OverlaySlot> DrawSlots;
        }

        public OverlayRenderPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public static void RegisterSlot(OverlaySlot slot)
        {
            if (slot != null && !Slots.Contains(slot))
            {
                Slots.Add(slot);
            }
        }

        public static void UnregisterSlot(OverlaySlot slot)
        {
            if (slot != null)
            {
                Slots.Remove(slot);
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!HasActiveSlot())
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<OverlayPassData>("MurimOverlayPass", out OverlayPassData passData))
            {
                passData.DrawSlots = Slots;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                if (resourceData.activeDepthTexture.IsValid())
                {
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                }

                builder.AllowPassCulling(false);
                builder.SetRenderFunc(RenderFunc);
            }
        }

        private static bool HasActiveSlot()
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                OverlaySlot slot = Slots[i];
                if (slot.Active && slot.Mesh != null && slot.Material != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ExecutePass(OverlayPassData data, RasterGraphContext context)
        {
            // Clear depth only so subsequent overlay draws pass depth test unconditionally.
            context.cmd.ClearRenderTarget(true, false, Color.clear);

            List<OverlaySlot> drawSlots = data.DrawSlots;
            for (int i = 0; i < drawSlots.Count; i++)
            {
                OverlaySlot slot = drawSlots[i];
                if (!slot.Active || slot.Mesh == null || slot.Material == null)
                {
                    continue;
                }

                context.cmd.DrawMesh(slot.Mesh, slot.Matrix, slot.Material, 0, 0, slot.PropertyBlock);
            }
        }
    }
}
