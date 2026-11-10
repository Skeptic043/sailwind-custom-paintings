using System;
using System.Collections.Generic;

namespace CustomPaintings
{
    internal enum FitMode
    {
        // Covers the canvas, cropping the picture's overhang.
        Fill,
        // Shows the whole picture, with the border colour around it.
        Fit,
        // Covers the canvas by distorting the picture.
        Stretch,
    }

    internal struct CanvasShape
    {
        public float Width, Height, CenterX, CenterY, Z;
        public float Aspect => Width / Height;
    }

    // Where a picture goes and which part of it shows. On the canvas, the canvas material samples
    // the picture directly. On the quad, a quad just in front of the canvas shows it, turned by the
    // painting's rotation, and the canvas shows the border colour.
    internal struct Placement
    {
        public bool OnQuad;
        // The quad's size before it is turned, in mesh units.
        public float QuadWidth, QuadHeight;
        public float ScaleX, ScaleY, OffsetX, OffsetY;
    }

    // Size, crop and placement arithmetic, kept free of Unity types so the logic tests can run it.
    internal static class PictureFit
    {
        // Each native frame's canvas quad (submesh 0), measured from the game's meshes in mesh
        // units. Every canvas maps the full 0-1 texture range upright and faces -Z, and no part
        // of the frame covers it. Native meshes are not readable at runtime.
        private static readonly Dictionary<string, CanvasShape> Canvases = new Dictionary<string, CanvasShape>
        {
            { "painting", new CanvasShape { Width = 1.331f, Height = 0.832f, Z = -0.009f } },
            { "painting_square", new CanvasShape { Width = 1.234f, Height = 1.165f, CenterX = 0.001f, CenterY = 0.0005f, Z = -0.010f } },
            { "painting_almost_square", new CanvasShape { Width = 1.097f, Height = 0.832f, Z = -0.009f } },
            { "painting_wide", new CanvasShape { Width = 1.531f, Height = 0.832f, Z = -0.009f } },
            { "painting_wide_wide", new CanvasShape { Width = 1.871f, Height = 0.832f, Z = -0.009f } },
            { "painting_vertical", new CanvasShape { Width = 1.032f, Height = 1.331f, Z = -0.009f } },
            { "painting_vertical_long", new CanvasShape { Width = 1.261f, Height = 1.837f, Z = -0.008f } },
        };

        public static bool TryGetCanvas(string meshName, out CanvasShape canvas)
        {
            canvas = default;
            if (string.IsNullOrEmpty(meshName)) return false;
            // A combined or instanced mesh can carry Unity's " Instance" suffix.
            string name = meshName.EndsWith(" Instance", StringComparison.Ordinal)
                ? meshName.Substring(0, meshName.Length - " Instance".Length) : meshName;
            return Canvases.TryGetValue(name, out canvas);
        }

        // Pictures keep their own size unless their longer side exceeds the limit. Sides are
        // rounded down to a multiple of 4, which texture compression needs.
        public static void TargetSize(int width, int height, int maxSide, out int targetWidth, out int targetHeight)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            // Integer arithmetic keeps the longer side exactly at the limit.
            long longest = Math.Max(width, height), side = Math.Min(longest, maxSide);
            targetWidth = (int)Math.Max(4, width * side / longest / 4 * 4);
            targetHeight = (int)Math.Max(4, height * side / longest / 4 * 4);
        }

        // Texture scale and offset for the canvas material. Fill samples the picture's centre
        // and crops the overhang; Stretch and Fit sample the whole picture.
        public static void Crop(FitMode mode, float pictureAspect, float canvasAspect,
            out float scaleX, out float scaleY, out float offsetX, out float offsetY)
        {
            scaleX = scaleY = 1f;
            offsetX = offsetY = 0f;
            if (mode != FitMode.Fill || pictureAspect <= 0f || canvasAspect <= 0f) return;
            if (pictureAspect > canvasAspect)
            {
                scaleX = canvasAspect / pictureAspect;
                offsetX = (1f - scaleX) / 2f;
            }
            else
            {
                scaleY = pictureAspect / canvasAspect;
                offsetY = (1f - scaleY) / 2f;
            }
        }

        // For Fit: the fraction of the canvas width and height the whole picture covers, centred.
        public static void FitSize(float pictureAspect, float canvasAspect, out float widthFraction, out float heightFraction)
        {
            widthFraction = heightFraction = 1f;
            if (pictureAspect <= 0f || canvasAspect <= 0f) return;
            if (pictureAspect > canvasAspect) heightFraction = canvasAspect / pictureAspect;
            else widthFraction = pictureAspect / canvasAspect;
        }

        // Clockwise quarter turns: 0, 90, 180 or 270. Anything else, for example from an edited
        // save, snaps to the nearest quarter turn.
        public static int Rotation(int degrees)
        {
            int quarters = (int)Math.Round(degrees / 90.0) % 4;
            return (quarters + 4) % 4 * 90;
        }

        // Upright and upside-down pictures in Fill or Stretch stay on the canvas material, where an
        // upside-down picture is its texture range reversed. Fit and sideways pictures need the quad.
        // A sideways picture is laid out in its own upright frame, where the canvas sides are swapped.
        public static Placement Place(FitMode mode, int rotation, float pictureAspect, float canvasWidth, float canvasHeight)
        {
            bool sideways = rotation == 90 || rotation == 270;
            var place = new Placement { OnQuad = mode == FitMode.Fit || sideways };
            float width = sideways ? canvasHeight : canvasWidth, height = sideways ? canvasWidth : canvasHeight;
            float aspect = height > 0f ? width / height : 0f;
            Crop(mode, pictureAspect, aspect, out place.ScaleX, out place.ScaleY, out place.OffsetX, out place.OffsetY);
            if (mode == FitMode.Fit)
            {
                FitSize(pictureAspect, aspect, out float widthFraction, out float heightFraction);
                width *= widthFraction;
                height *= heightFraction;
            }
            place.QuadWidth = width;
            place.QuadHeight = height;
            if (rotation == 180 && !place.OnQuad)
            {
                place.OffsetX += place.ScaleX;
                place.OffsetY += place.ScaleY;
                place.ScaleX = -place.ScaleX;
                place.ScaleY = -place.ScaleY;
            }
            return place;
        }

        // "#RRGGBB" to 0-1 channels. Anything unreadable is black.
        public static void ParseColor(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return;
            if (!int.TryParse(hex.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out int value)) return;
            r = ((value >> 16) & 0xFF) / 255f;
            g = ((value >> 8) & 0xFF) / 255f;
            b = (value & 0xFF) / 255f;
        }
    }
}
