using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CustomPaintings
{
    // Added to every native painting when it loads. Swaps the canvas material (slot 0) for a
    // copy showing the chosen picture and puts the original material back on request. Fit mode
    // colours the canvas and shows the picture on a thin quad just in front of it. The frame
    // material and the painting item itself are never changed.
    internal sealed class PaintingPicture : MonoBehaviour
    {
        private static readonly HashSet<string> reportedMeshes = new HashSet<string>();
        private ShipItem item;
        private SaveablePrefab saveable;
        private Renderer canvasRenderer;
        private Material[] originals;
        private Material custom;
        private CanvasShape canvas;
        private bool knownCanvas;
        private Texture2D picture;
        private float pictureAspect;
        private MeshRenderer overlay;
        private Material overlayMaterial;
        private bool failureReported;

        // The picture this painting shows or is waiting for, or null for its own art.
        public string Path { get; private set; }
        public FitMode Mode { get; private set; } = FitMode.Fill;
        public string Background { get; private set; } = PictureRecord.DefaultBackground;
        public bool CanChoose => item != null && item.sold && saveable != null && saveable.instanceId != 0;

        // All 34 native paintings are plain ShipItems named "painting" with the canvas first.
        public static bool IsPainting(ShipItem item) =>
            item != null && item.GetType() == typeof(ShipItem) && item.name == "painting" &&
            item.GetComponent<Renderer>() != null && item.GetComponent<Renderer>().sharedMaterials.Length >= 1;

        public void Init(ShipItem shipItem)
        {
            item = shipItem;
            saveable = item.GetComponent<SaveablePrefab>();
            canvasRenderer = item.GetComponent<Renderer>();
            originals = canvasRenderer.sharedMaterials;
            var mesh = item.GetComponent<MeshFilter>()?.sharedMesh;
            string meshName = mesh != null ? mesh.name : "(no mesh)";
            knownCanvas = PictureFit.TryGetCanvas(meshName, out canvas);
            if (!knownCanvas && mesh != null && mesh.bounds.size.y > 0f)
                canvas = new CanvasShape { Width = mesh.bounds.size.x, Height = mesh.bounds.size.y };
            if (reportedMeshes.Add(meshName))
                Plugin.Log.LogInfo(knownCanvas
                    ? $"Frame {meshName}: canvas {canvas.Width:0.000} x {canvas.Height:0.000}, aspect {canvas.Aspect:0.000}."
                    : $"Frame {meshName} is not a known painting frame. Its shape is estimated from its outline, and Fit shows as Fill.");
            if (saveable != null && saveable.instanceId != 0 &&
                Plugin.Store.TryGet(saveable.instanceId, saveable.prefabIndex, out var record))
            {
                Mode = record.FitMode;
                Background = record.BackgroundColor;
                Show(record.Path);
            }
        }

        public void Choose(string path, FitMode mode, string background)
        {
            if (!CanChoose) return;
            Mode = mode;
            Background = background;
            Plugin.Store.Set(saveable.instanceId, saveable.prefabIndex, path, mode, background);
            failureReported = false;
            // Choosing the same picture again retries a file that could not be loaded.
            if (path == Path && picture == null) Show(null, false);
            if (path == Path) Apply();
            else Show(path);
        }

        // Changes how the current picture sits in its frame.
        public void SetFit(FitMode mode, string background)
        {
            if (Path == null) return;
            Choose(Path, mode, background);
        }

        public void ChooseOriginal()
        {
            if (saveable != null) Plugin.Store.Remove(saveable.instanceId);
            Show(null, true);
        }

        private void Show(string path, bool restore = true)
        {
            if (path == Path) return;
            PictureTextures.Instance?.Release(Path, this);
            Path = path;
            picture = null;
            if (path == null) { if (restore) RestoreOriginal(); }
            else PictureTextures.Instance?.Acquire(path, this);
        }

        public void OnPictureReady(Texture2D texture, float aspect)
        {
            picture = texture;
            pictureAspect = aspect;
            Apply();
        }

        private void Apply()
        {
            if (canvasRenderer == null || picture == null) return;
            if (custom == null) custom = new Material(originals[0]) { name = originals[0].name + " (custom picture)" };
            var mode = Mode == FitMode.Fit && !knownCanvas ? FitMode.Fill : Mode;
            bool fit = mode == FitMode.Fit;
            if (custom.HasProperty("_Color"))
            {
                if (fit)
                {
                    PictureFit.ParseColor(Background, out float r, out float g, out float b);
                    custom.color = new Color(r, g, b, 1f);
                }
                else custom.color = originals[0].HasProperty("_Color") ? originals[0].color : Color.white;
            }
            custom.mainTexture = fit ? Texture2D.whiteTexture : (Texture)picture;
            PictureFit.Crop(mode, pictureAspect, canvas.Aspect, out float sx, out float sy, out float ox, out float oy);
            custom.mainTextureScale = new Vector2(sx, sy);
            custom.mainTextureOffset = new Vector2(ox, oy);
            var materials = (Material[])originals.Clone();
            materials[0] = custom;
            canvasRenderer.sharedMaterials = materials;

            if (fit)
            {
                EnsureOverlay();
                overlayMaterial.mainTexture = picture;
                PictureFit.FitSize(pictureAspect, canvas.Aspect, out float wf, out float hf);
                // A few millimetres in front of the canvas, toward the viewer on -Z.
                overlay.transform.localPosition = new Vector3(canvas.CenterX, canvas.CenterY, canvas.Z - 0.003f);
                overlay.transform.localScale = new Vector3(canvas.Width * wf, canvas.Height * hf, 1f);
                overlay.enabled = true;
            }
            else if (overlay != null) overlay.enabled = false;
        }

        private void EnsureOverlay()
        {
            if (overlay != null) return;
            var go = new GameObject("Custom Paintings picture");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad.Mesh;
            overlay = go.AddComponent<MeshRenderer>();
            overlay.shadowCastingMode = ShadowCastingMode.Off;
            overlayMaterial = new Material(originals[0]) { name = originals[0].name + " (custom picture overlay)" };
            if (overlayMaterial.HasProperty("_Color"))
                overlayMaterial.color = originals[0].HasProperty("_Color") ? originals[0].color : Color.white;
            overlayMaterial.mainTextureScale = Vector2.one;
            overlayMaterial.mainTextureOffset = Vector2.zero;
            overlay.sharedMaterial = overlayMaterial;
            // The native LOD group lists only the canvas renderer. Without this the picture would
            // stay visible after the painting is culled at a distance.
            var lods = GetComponent<LODGroup>();
            if (lods != null)
            {
                var levels = lods.GetLODs();
                if (levels.Length > 0)
                {
                    var renderers = new List<Renderer>(levels[0].renderers ?? new Renderer[0]) { overlay };
                    levels[0].renderers = renderers.ToArray();
                    lods.SetLODs(levels);
                }
            }
        }

        // The record stays, so the picture returns once the file can be read again.
        public void OnPictureFailed()
        {
            picture = null;
            RestoreOriginal();
            if (failureReported) return;
            failureReported = true;
            Plugin.Log.LogWarning("A painting keeps its original art because its picture could not be loaded: " + Path);
        }

        private void RestoreOriginal()
        {
            if (canvasRenderer != null && originals != null) canvasRenderer.sharedMaterials = originals;
            if (overlay != null) overlay.enabled = false;
            if (custom != null) Destroy(custom);
            custom = null;
        }

        private void OnDestroy()
        {
            PictureTextures.Instance?.Release(Path, this);
            if (custom != null) Destroy(custom);
            if (overlayMaterial != null) Destroy(overlayMaterial);
        }
    }

    // One unit quad in the XY plane facing -Z, the side the paintings and menus face.
    internal static class Quad
    {
        private static Mesh mesh;

        public static Mesh Mesh
        {
            get
            {
                if (mesh != null) return mesh;
                mesh = new Mesh { name = "Custom Paintings quad" };
                mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0f), new Vector3(.5f, -.5f, 0f), new Vector3(.5f, .5f, 0f), new Vector3(-.5f, .5f, 0f) };
                mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                mesh.tangents = new[] { new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f) };
                // Clockwise as seen from -Z, so the front face points at the viewer.
                mesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
