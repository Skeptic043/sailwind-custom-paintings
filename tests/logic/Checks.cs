using System;
using System.IO;
using System.Linq;

namespace CustomPaintings
{
    internal static class Checks
    {
        private static int failures;

        private static void Check(bool condition, string name)
        {
            Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name);
            if (!condition) failures++;
        }

        private static bool Near(float a, float b, float tolerance = 0.0005f) => Math.Abs(a - b) < tolerance;

        private static int Main()
        {
            Sizes();
            Cropping();
            Rotating();
            CanvasShapes();
            Saves();
            Folders();
            Console.WriteLine(failures == 0 ? "All logic checks passed." : failures + " logic check(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        private static void Sizes()
        {
            PictureFit.TargetSize(1600, 1200, 2048, out int w, out int h);
            Check(w == 1600 && h == 1200, "a picture within the limit keeps its size");
            PictureFit.TargetSize(1001, 751, 2048, out w, out h);
            Check(w == 1000 && h == 748, "sides round down to a multiple of 4 for compression");
            PictureFit.TargetSize(4000, 3000, 2048, out w, out h);
            Check(w == 2048 && h == 1536, "a large landscape picture shrinks its longer side to the limit");
            PictureFit.TargetSize(3000, 6000, 2048, out w, out h);
            Check(w == 1024 && h == 2048, "a large portrait picture shrinks its longer side to the limit");
            PictureFit.TargetSize(20000, 2, 192, out w, out h);
            Check(w == 192 && h == 4, "an extreme panorama still gets a usable texture size");
        }

        private static void Cropping()
        {
            // A 2:1 picture on a 1:1 canvas shows its middle half.
            PictureFit.Crop(FitMode.Fill, 2f, 1f, out float sx, out float sy, out float ox, out float oy);
            Check(Near(sx, 0.5f) && Near(sy, 1f) && Near(ox, 0.25f) && Near(oy, 0f), "Fill crops a wide picture evenly at the sides");
            PictureFit.Crop(FitMode.Fill, 0.5f, 1.6f, out sx, out sy, out ox, out oy);
            Check(Near(sx, 1f) && Near(sy, 0.3125f) && Near(oy, 0.34375f), "Fill crops a tall picture evenly at the top and bottom");
            // Whatever the shapes, the visible part of the picture has the canvas's shape.
            foreach (float picture in new[] { 0.4f, 0.75f, 1f, 1.5f, 3f })
            foreach (float canvas in new[] { 0.686f, 1.059f, 2.249f })
            {
                PictureFit.Crop(FitMode.Fill, picture, canvas, out sx, out sy, out ox, out oy);
                Check(Near(picture * sx / sy, canvas) && Near(ox * 2 + sx, 1f) && Near(oy * 2 + sy, 1f),
                    $"Fill: picture {picture} on canvas {canvas} covers the canvas without stretching");
                PictureFit.FitSize(picture, canvas, out float wf, out float hf);
                Check(Near(canvas * wf / hf, picture) && Math.Max(wf, hf) == 1f && wf <= 1f && hf <= 1f,
                    $"Fit: picture {picture} on canvas {canvas} shows whole, undistorted, touching two edges");
            }
            foreach (var mode in new[] { FitMode.Fit, FitMode.Stretch })
            {
                PictureFit.Crop(mode, 2f, 1f, out sx, out sy, out ox, out oy);
                Check(sx == 1f && sy == 1f && ox == 0f && oy == 0f, mode + " samples the whole picture");
            }
            PictureFit.Crop(FitMode.Fill, 1f, 1f, out sx, out sy, out ox, out oy);
            Check(sx == 1f && sy == 1f && ox == 0f && oy == 0f, "a matching picture is not cropped");
        }

        private static void Rotating()
        {
            Check(PictureFit.Rotation(0) == 0 && PictureFit.Rotation(90) == 90 && PictureFit.Rotation(360) == 0 &&
                PictureFit.Rotation(-90) == 270 && PictureFit.Rotation(100) == 90 && PictureFit.Rotation(450) == 90,
                "rotations are quarter turns from 0 to 270");

            var upright = PictureFit.Place(FitMode.Fill, 0, 2f, 1.6f, 1f);
            PictureFit.Crop(FitMode.Fill, 2f, 1.6f, out float sx, out float sy, out float ox, out float oy);
            Check(!upright.OnQuad && upright.ScaleX == sx && upright.OffsetX == ox && upright.ScaleY == sy && upright.OffsetY == oy,
                "an upright Fill picture stays on the canvas with the same crop as before");

            var flipped = PictureFit.Place(FitMode.Fill, 180, 2f, 1.6f, 1f);
            Check(!flipped.OnQuad && Near(flipped.ScaleX, -sx) && Near(flipped.OffsetX, ox + sx) &&
                Near(flipped.ScaleY, -1f) && Near(flipped.OffsetY, 1f),
                "an upside-down picture stays on the canvas with its crop reversed");

            // A 4:3 photo turned onto a tall 1 x 1.6 canvas fills it as a 1.6 x 1 frame of its own.
            var sideways = PictureFit.Place(FitMode.Fill, 90, 4f / 3f, 1f, 1.6f);
            Check(sideways.OnQuad && Near(sideways.QuadWidth, 1.6f) && Near(sideways.QuadHeight, 1f) &&
                Near(4f / 3f * sideways.ScaleX / sideways.ScaleY, 1.6f) && Near(sideways.ScaleX, 1f),
                "a sideways picture covers the canvas on a quad, cropped to the turned canvas shape");

            var fitSideways = PictureFit.Place(FitMode.Fit, 270, 2f, 1f, 1.6f);
            Check(fitSideways.OnQuad && Near(fitSideways.QuadWidth, 1.6f) && Near(fitSideways.QuadHeight, .8f) &&
                fitSideways.ScaleX == 1f && fitSideways.ScaleY == 1f,
                "a sideways Fit picture shows whole, sized within the turned canvas");

            var fitFlipped = PictureFit.Place(FitMode.Fit, 180, 2f, 1.6f, 1f);
            Check(fitFlipped.OnQuad && fitFlipped.ScaleX == 1f && fitFlipped.OffsetX == 0f,
                "an upside-down Fit picture is turned with its quad, not its texture range");
        }

        private static void CanvasShapes()
        {
            Check(PictureFit.TryGetCanvas("painting_vertical_long", out var canvas) && Near(canvas.Aspect, 0.686f, 0.001f),
                "each native frame mesh has a known canvas shape");
            Check(PictureFit.TryGetCanvas("painting_wide Instance", out canvas) && Near(canvas.Aspect, 1.841f, 0.001f),
                "an instanced mesh name still finds its frame");
            Check(!PictureFit.TryGetCanvas("some_mod_frame", out _) && !PictureFit.TryGetCanvas(null, out _),
                "an unknown mesh is left to the outline fallback");
            PictureFit.ParseColor("#5A1E1E", out float r, out float g, out float b);
            Check(Near(r, 90 / 255f) && Near(g, 30 / 255f) && Near(b, 30 / 255f), "border colours are read from #RRGGBB");
            PictureFit.ParseColor("red", out r, out g, out b);
            Check(r == 0f && g == 0f && b == 0f, "an unreadable border colour is black");
        }

        private static void Saves()
        {
            var store = new PictureStore();
            store.Set(-1234, 120, @"D:\Pictures\boat.png", FitMode.Fill, PictureRecord.DefaultBackground);
            store.Set(99, 261, @"D:\Pictures\Long name ünïcode.jpg", FitMode.Fit, "#E8DCC0");
            store.Set(7, 250, @"D:\Pictures\tall.png", FitMode.Fill, null, 270);
            string json = store.Serialize();
            Check(!json.Contains("Fill") && !json.Contains("#000000") && json.Split(new[] { "Rotation" }, StringSplitOptions.None).Length == 2,
                "default mode, border and rotation are left out of the save");
            var loaded = new PictureStore();
            Check(loaded.Load(json) && loaded.Count == 3 && loaded.Writable, "saved pictures load back");
            Check(loaded.TryGet(7, 250, out var turned) && turned.TurnDegrees == 270, "a rotation round-trips");
            Check(loaded.TryGet(99, 261, out var record) && record.Path == @"D:\Pictures\Long name ünïcode.jpg" &&
                record.FitMode == FitMode.Fit && record.BackgroundColor == "#E8DCC0",
                "a picture path, fit mode and border colour round-trip exactly");
            Check(loaded.TryGet(-1234, 120, out record) && record.FitMode == FitMode.Fill && record.BackgroundColor == "#000000" && record.TurnDegrees == 0,
                "a record without a mode, border or rotation means upright Fill on black");
            Check(!loaded.TryGet(-1234, 121, out _), "a different painting under the same ID gets no picture");
            loaded.Remove(-1234);
            Check(!loaded.TryGet(-1234, 120, out _) && loaded.Count == 2, "restoring the original art removes the record");
            var future = new PictureStore();
            Check(future.Load("{\"Schema\":1,\"Pictures\":[{\"InstanceId\":5,\"PrefabIndex\":1,\"Path\":\"a.png\",\"Mode\":\"Tile\"}]}") &&
                future.TryGet(5, 1, out record) && record.FitMode == FitMode.Fill, "an unknown fit mode shows as Fill");

            Check(new PictureStore().Load(null), "a save without picture data loads");
            foreach (string bad in new[] { "{not json", "{\"Schema\":2,\"Pictures\":[]}",
                "{\"Schema\":1,\"Pictures\":[{\"InstanceId\":0,\"PrefabIndex\":1,\"Path\":\"a.png\"}]}",
                "{\"Schema\":1,\"Pictures\":[{\"InstanceId\":5,\"PrefabIndex\":1,\"Path\":\"a.png\"},{\"InstanceId\":5,\"PrefabIndex\":1,\"Path\":\"b.png\"}]}" })
            {
                var refused = new PictureStore();
                Check(!refused.Load(bad) && !refused.Writable && refused.Count == 0,
                    "unreadable or unknown picture data is refused and left unwritten: " + bad);
            }
            var replaced = new PictureStore();
            replaced.Load("{not json");
            replaced.Set(7, 120, "a.png", FitMode.Fill, null);
            Check(replaced.Writable && replaced.Count == 1, "choosing a picture replaces unreadable saved data so new choices are saved");
        }

        private static void Folders()
        {
            var parsed = PictureLibrary.ParseFolders(@" D:\Pictures | ""E:\Ship Art"" ||D:\Pictures ");
            Check(parsed.SequenceEqual(new[] { @"D:\Pictures", @"E:\Ship Art" }), "folders are split, trimmed, unquoted and deduplicated");
            Check(PictureLibrary.ParseFolders("").Count == 0, "no folders configured");

            var names = new[] { "photo 10.jpg", "Photo 2.jpg", "photo 1.jpg", "beach.png", "photo 02b.jpg" }.ToList();
            names.Sort(PictureLibrary.NaturalCompare);
            Check(names.SequenceEqual(new[] { "beach.png", "photo 1.jpg", "Photo 2.jpg", "photo 02b.jpg", "photo 10.jpg" }),
                "names sort the way people number files, ignoring case");

            string root = Path.Combine(Path.GetTempPath(), "custompaintings-checks-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Trip 10", "deeper"));
                Directory.CreateDirectory(Path.Combine(root, "Trip 9"));
                Directory.CreateDirectory(Path.Combine(root, "zebra"));
                foreach (string file in new[] { "b.PNG", "a.jpg", "notes.txt", "Trip 10/c.jpeg", "Trip 10/deeper/d.png", "trip map.png" })
                    File.WriteAllBytes(Path.Combine(root, file), new byte[0]);
                var entries = PictureLibrary.List(root, out string problem);
                var listed = entries.Select(e => (e.IsFolder ? "folder:" : "") + e.Name).ToList();
                Check(problem == null && listed.SequenceEqual(new[] { "folder:Trip 9", "folder:Trip 10", "folder:zebra", "a", "b", "trip map" }),
                    "a folder lists its subfolders first, then its own PNG and JPG pictures, without reading deeper");
                var matches = PictureLibrary.Filter(entries, " TRIP ").Select(e => e.Name).ToList();
                Check(matches.SequenceEqual(new[] { "Trip 9", "Trip 10", "trip map" }),
                    "search matches folders and pictures in the open folder only, ignoring case and spaces around the query");
                Check(PictureLibrary.Filter(entries, "").Count == entries.Count, "an empty search shows everything");
            }
            finally { Directory.Delete(root, true); }
            var missing = PictureLibrary.List(Path.Combine(root, "gone"), out string missingProblem);
            Check(missing.Count == 0 && missingProblem != null, "a missing folder reports a problem instead of failing");
            Check(PictureLibrary.PageCount(0, 10) == 1 && PictureLibrary.PageCount(10, 10) == 1 && PictureLibrary.PageCount(11, 10) == 2,
                "page counts round up and never drop below one");
        }
    }
}
