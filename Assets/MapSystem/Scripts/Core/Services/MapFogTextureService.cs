using System;
using UnityEngine;
using UnityEngine.UI;

namespace Artifika.Map.Core
{
    public sealed class MapFogTextureService : MonoBehaviour
    {
        private static readonly int FogMaskId = Shader.PropertyToID("_FogMask");

        [SerializeField, Range(64, 1024)] private int defaultResolution = 256;

        private Texture2D fogTexture;
        private Texture2D stagingTexture;
        private byte[] stagingBuffer;
        private Color32[] uploadBuffer;
        private Material overlayMaterial;
        private Material originalOverlayMaterial;
        private Graphic overlayGraphic;

        public static MapFogTextureService Instance { get; private set; }
        public static event Action<MapFogTextureService> InstanceChanged;

        public Texture MaskTexture => fogTexture;

        public void Initialize(MapWorldDefinition definition)
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                return;
            }

            Instance = this;
            EnsureTexture(defaultResolution);
            InstanceChanged?.Invoke(this);
        }

        // Single source of truth: the display always reflects the CPU mask that gets saved.
        public void SetMask(byte[] mask, int resolution)
        {
            if (mask == null || mask.Length != resolution * resolution)
            {
                GameLog.Warning("[MapFogTextureService] Ignored an invalid fog mask snapshot.");
                return;
            }

            EnsureTexture(resolution);
            Upload(mask);
            RefreshOverlayMask();
        }

        /// <summary>
        /// Uploads only the texels a reveal actually touched.
        ///
        /// <see cref="Texture2D.Apply"/> always re-uploads the entire texture, which at 1024²
        /// is 1 MB every time the player takes a step. A reveal disc is a couple of hundred
        /// bytes, so this stages the patch in a small texture and blits it into place — the
        /// cost stops scaling with mask resolution.
        ///
        /// Falls back to a full upload when the region is unusable or the hardware cannot do
        /// partial copies.
        /// </summary>
        public void SetMaskRegion(byte[] mask, int resolution, RectInt region)
        {
            if (mask == null || mask.Length != resolution * resolution)
            {
                GameLog.Warning("[MapFogTextureService] Ignored an invalid fog mask patch.");
                return;
            }

            EnsureTexture(resolution);

            if (fogTexture == null || fogTexture.width != resolution)
            {
                Upload(mask);
                RefreshOverlayMask();
                return;
            }

            region = ClampRegion(region, resolution);

            if (region.width <= 0 || region.height <= 0 || !CanCopyRegions)
            {
                Upload(mask);
                RefreshOverlayMask();
                return;
            }

            if (!TryUploadRegion(mask, resolution, region))
                Upload(mask);

            RefreshOverlayMask();
        }

        private static bool CanCopyRegions =>
            (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;

        private static RectInt ClampRegion(RectInt region, int resolution)
        {
            int xMin = Mathf.Clamp(region.xMin, 0, resolution);
            int yMin = Mathf.Clamp(region.yMin, 0, resolution);
            int xMax = Mathf.Clamp(region.xMax, 0, resolution);
            int yMax = Mathf.Clamp(region.yMax, 0, resolution);
            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private bool TryUploadRegion(byte[] mask, int resolution, RectInt region)
        {
            if (!EnsureStagingTexture(region.width, region.height))
                return false;

            int bytesPerTexel = fogTexture.format == TextureFormat.R8 ? 1 : 4;
            int needed = region.width * region.height * bytesPerTexel;

            // LoadRawTextureData rejects a mismatched length, so this must be exact rather than
            // merely large enough.
            if (stagingBuffer == null || stagingBuffer.Length != needed)
                stagingBuffer = new byte[needed];

            for (int row = 0; row < region.height; row++)
            {
                int source = (region.yMin + row) * resolution + region.xMin;
                int destination = row * region.width * bytesPerTexel;

                if (bytesPerTexel == 1)
                {
                    Buffer.BlockCopy(mask, source, stagingBuffer, destination, region.width);
                    continue;
                }

                for (int x = 0; x < region.width; x++)
                {
                    byte v = mask[source + x];
                    int o = destination + x * 4;
                    stagingBuffer[o] = v;
                    stagingBuffer[o + 1] = v;
                    stagingBuffer[o + 2] = v;
                    stagingBuffer[o + 3] = byte.MaxValue;
                }
            }

            stagingTexture.LoadRawTextureData(stagingBuffer);
            stagingTexture.Apply(false, false);

            Graphics.CopyTexture(
                stagingTexture, 0, 0, 0, 0, region.width, region.height,
                fogTexture, 0, 0, region.xMin, region.yMin);

            return true;
        }

        private bool EnsureStagingTexture(int width, int height)
        {
            if (fogTexture == null) return false;

            if (stagingTexture != null &&
                stagingTexture.width == width &&
                stagingTexture.height == height &&
                stagingTexture.format == fogTexture.format)
            {
                return true;
            }

            if (stagingTexture != null)
                Destroy(stagingTexture);

            stagingTexture = new Texture2D(width, height, fogTexture.format, mipChain: false, linear: true)
            {
                name = "MapFogMaskPatch",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            return true;
        }

        // Called by MapFogOverlayBinder when the overlay Graphic is enabled.
        public void BindOverlay(Graphic graphic)
        {
            if (graphic == null || overlayGraphic == graphic)
                return;

            UnbindOverlay(overlayGraphic);

            overlayGraphic = graphic;
            originalOverlayMaterial = graphic.material;
            overlayMaterial = new Material(originalOverlayMaterial)
            {
                name = $"{originalOverlayMaterial.name} (Runtime)"
            };

            overlayGraphic.material = overlayMaterial;
            RefreshOverlayMask();
        }

        public void UnbindOverlay(Graphic graphic)
        {
            if (graphic == null || overlayGraphic != graphic)
                return;

            if (overlayGraphic.material == overlayMaterial)
                overlayGraphic.material = originalOverlayMaterial;

            if (overlayMaterial != null)
                Destroy(overlayMaterial);

            overlayGraphic = null;
            overlayMaterial = null;
            originalOverlayMaterial = null;
        }

        private void EnsureTexture(int resolution)
        {
            resolution = Mathf.Clamp(
                resolution,
                MapFogMaskCodec.MinResolution,
                MapFogMaskCodec.MaxResolution);

            if (fogTexture != null && fogTexture.width == resolution)
                return;

            if (fogTexture != null)
                Destroy(fogTexture);

            // Fog masks store reveal strength, not color: keep them linear.
            TextureFormat format = SystemInfo.SupportsTextureFormat(TextureFormat.R8)
                ? TextureFormat.R8
                : TextureFormat.RGBA32;

            fogTexture = new Texture2D(resolution, resolution, format, mipChain: false, linear: true)
            {
                name = "MapFogMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            uploadBuffer = format == TextureFormat.RGBA32
                ? new Color32[resolution * resolution]
                : null;

            Upload(new byte[resolution * resolution]);
            RefreshOverlayMask();
        }

        private void Upload(byte[] mask)
        {
            if (fogTexture.format == TextureFormat.R8)
            {
                fogTexture.LoadRawTextureData(mask);
            }
            else
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    byte v = mask[i];
                    uploadBuffer[i] = new Color32(v, v, v, byte.MaxValue);
                }

                fogTexture.SetPixels32(uploadBuffer);
            }

            fogTexture.Apply(false, false);
        }

        private void RefreshOverlayMask()
        {
            if (overlayMaterial == null || fogTexture == null)
                return;

            overlayMaterial.SetTexture(FogMaskId, fogTexture);
            overlayGraphic?.SetMaterialDirty();
        }

        private void OnDestroy()
        {
            UnbindOverlay(overlayGraphic);

            if (fogTexture != null)
                Destroy(fogTexture);

            if (stagingTexture != null)
                Destroy(stagingTexture);

            if (Instance == this)
            {
                Instance = null;
                InstanceChanged?.Invoke(null);
            }
        }
    }
}