using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomPaintings
{
    // Loads picture files into textures, one at a time so no single frame does much work.
    // Unity's texture download handler decodes the file. The result is drawn on the GPU into
    // a texture of the final size, with mipmaps for distance viewing, then optionally
    // compressed. Each file is loaded once and shared by every painting that shows it.
    internal sealed class PictureTextures : MonoBehaviour
    {
        public const int ThumbnailSide = 192;

        private sealed class Entry
        {
            public Texture2D Texture;
            public float Aspect;
            public bool Loading;
            public bool Failed;
            public bool ReloadQueued;
            public readonly HashSet<PaintingPicture> Owners = new HashSet<PaintingPicture>();
        }

        private readonly Dictionary<string, Entry> pictures = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> pictureQueue = new Queue<string>();
        private readonly Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> failedThumbnails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> wantedThumbnails = new List<string>();
        private string loadingThumbnail;
        private bool working;

        internal static PictureTextures Instance { get; private set; }

        private void Awake() => Instance = this;

        // The painting is told once the picture is ready, and again if it is reloaded.
        public void Acquire(string path, PaintingPicture owner)
        {
            if (!pictures.TryGetValue(path, out var entry))
            {
                entry = new Entry();
                pictures.Add(path, entry);
            }
            entry.Owners.Add(owner);
            if (entry.Texture != null) owner.OnPictureReady(entry.Texture, entry.Aspect);
            else
            {
                // A file that failed earlier is tried again, in case it has been put back.
                entry.Failed = false;
                Enqueue(path, entry);
            }
        }

        public void Release(string path, PaintingPicture owner)
        {
            if (path == null || !pictures.TryGetValue(path, out var entry)) return;
            entry.Owners.Remove(owner);
            if (entry.Owners.Count > 0 || entry.Loading) return;
            if (entry.Texture != null) Destroy(entry.Texture);
            pictures.Remove(path);
        }

        // After a size or compression setting changes, every picture in use is loaded again.
        // Paintings keep showing the old texture until its replacement is ready.
        public void ReloadAll()
        {
            foreach (var pair in pictures) ReloadEntry(pair.Key, pair.Value);
        }

        private void ReloadEntry(string path, Entry entry)
        {
            entry.Failed = false;
            // A load already running used the old settings, so it runs again when it finishes.
            if (entry.Loading) entry.ReloadQueued = true;
            else Enqueue(path, entry);
        }

        // The picker names the thumbnails currently on screen. Ones it no longer shows are not loaded.
        public Texture2D Thumbnail(string path)
        {
            thumbnails.TryGetValue(path, out var texture);
            return texture;
        }

        public bool ThumbnailFailed(string path) => failedThumbnails.Contains(path);

        // The picker clears its thumbnails when it closes, so browsing never accumulates memory.
        public void ClearThumbnails()
        {
            wantedThumbnails.Clear();
            foreach (var texture in thumbnails.Values) Destroy(texture);
            thumbnails.Clear();
            failedThumbnails.Clear();
        }

        public void WantThumbnails(List<string> paths)
        {
            wantedThumbnails.Clear();
            foreach (string path in paths)
                if (!thumbnails.ContainsKey(path) && !failedThumbnails.Contains(path) &&
                    !string.Equals(path, loadingThumbnail, StringComparison.OrdinalIgnoreCase)) wantedThumbnails.Add(path);
            Pump();
        }

        private void Enqueue(string path, Entry entry)
        {
            if (entry.Loading) return;
            entry.Loading = true;
            pictureQueue.Enqueue(path);
            Pump();
        }

        private void Pump()
        {
            if (!working && (pictureQueue.Count > 0 || wantedThumbnails.Count > 0)) StartCoroutine(Work());
        }

        private IEnumerator Work()
        {
            working = true;
            try
            {
                while (true)
                {
                    if (pictureQueue.Count > 0)
                    {
                        string path = pictureQueue.Dequeue();
                        yield return LoadPicture(path);
                    }
                    else if (wantedThumbnails.Count > 0)
                    {
                        string path = wantedThumbnails[0];
                        wantedThumbnails.RemoveAt(0);
                        yield return LoadThumbnail(path);
                    }
                    else break;
                    // Spread the GPU readback and compression of consecutive pictures across frames.
                    yield return null;
                }
            }
            finally { working = false; }
        }

        private IEnumerator LoadPicture(string path)
        {
            if (!pictures.TryGetValue(path, out var entry)) yield break;
            Texture2D result = null;
            float aspect = 0f;
            yield return Decode(path, Plugin.MaxSize, true, Plugin.Compress, (texture, sourceAspect) => { result = texture; aspect = sourceAspect; });
            entry.Loading = false;
            // Every painting released it while it loaded.
            if (entry.Owners.Count == 0)
            {
                if (result != null) Destroy(result);
                if (entry.Texture != null) Destroy(entry.Texture);
                pictures.Remove(path);
                yield break;
            }
            // Settings changed while this load was running.
            bool reload = entry.ReloadQueued;
            entry.ReloadQueued = false;
            if (result == null)
            {
                // A failed reload keeps the picture that is already showing.
                if (entry.Texture == null)
                {
                    entry.Failed = true;
                    foreach (var owner in new List<PaintingPicture>(entry.Owners)) owner.OnPictureFailed();
                }
            }
            else
            {
                var old = entry.Texture;
                entry.Texture = result;
                entry.Aspect = aspect;
                foreach (var owner in new List<PaintingPicture>(entry.Owners)) owner.OnPictureReady(result, aspect);
                if (old != null) Destroy(old);
            }
            if (reload) ReloadEntry(path, entry);
        }

        private IEnumerator LoadThumbnail(string path)
        {
            Texture2D result = null;
            loadingThumbnail = path;
            yield return Decode(path, ThumbnailSide, false, true, (texture, _) => result = texture);
            loadingThumbnail = null;
            if (result != null && thumbnails.ContainsKey(path)) { Destroy(result); yield break; }
            // Cleared while loading: the picker has closed.
            if (result != null && (PicturePicker.Instance == null || !PicturePicker.Instance.IsOpen)) Destroy(result);
            else if (result != null) thumbnails[path] = result;
            else failedThumbnails.Add(path);
        }

        private static IEnumerator Decode(string path, int maxSide, bool mipmaps, bool compress, Action<Texture2D, float> done)
        {
            string uri;
            try { uri = new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri; }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Not a usable picture path {path}: {exception.Message}");
                done(null, 0f);
                yield break;
            }
            // Non-readable: the decoded image goes straight to the GPU without a CPU copy.
            using (var request = UnityWebRequestTexture.GetTexture(uri, true))
            {
                yield return request.SendWebRequest();
                var texture = Prepare(path, request, maxSide, mipmaps, compress, out float aspect);
                done(texture, aspect);
            }
        }

        // An exception escaping a coroutine would stop the loading queue for the session, so a
        // picture that cannot be read or prepared, such as one too large for the GPU, fails on its own.
        private static Texture2D Prepare(string path, UnityWebRequest request, int maxSide, bool mipmaps, bool compress, out float aspect)
        {
            aspect = 0f;
            Texture2D source = null;
            try
            {
                if (request.isNetworkError || request.isHttpError)
                {
                    Plugin.Log.LogWarning($"Could not read picture {path}: {request.error}");
                    return null;
                }
                source = DownloadHandlerTexture.GetContent(request);
                if (source == null || source.width <= 0 || source.height <= 0)
                {
                    Plugin.Log.LogWarning("Could not decode picture " + path);
                    return null;
                }
                aspect = source.width / (float)source.height;
                PictureFit.TargetSize(source.width, source.height, maxSide, out int width, out int height);
                return Resample(source, width, height, mipmaps, compress);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning($"Could not prepare picture {path}: {exception.Message}");
                return null;
            }
            finally
            {
                if (source != null) Destroy(source);
            }
        }

        // Halving steps keep a large reduction from skipping pixels. sRGB render textures keep
        // colours unchanged in a linear-colour project and are ignored in a gamma one.
        private static Texture2D Resample(Texture source, int width, int height, bool mipmaps, bool compress)
        {
            RenderTexture step = null, final = null;
            Texture2D texture = null;
            var previous = RenderTexture.active;
            try
            {
                Texture current = source;
                int w = source.width, h = source.height;
                while (w / 2 >= width && h / 2 >= height)
                {
                    w /= 2;
                    h /= 2;
                    var next = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    next.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(current, next);
                    if (step != null) RenderTexture.ReleaseTemporary(step);
                    current = step = next;
                }
                final = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(current, final);

                texture = new Texture2D(width, height, TextureFormat.RGB24, mipmaps, false);
                RenderTexture.active = final;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                RenderTexture.active = previous;

                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
                texture.anisoLevel = mipmaps ? 4 : 0;
                if (compress)
                {
                    texture.Apply(mipmaps, false);
                    texture.Compress(false);
                    texture.Apply(false, true);
                }
                else texture.Apply(mipmaps, true);
                var result = texture;
                texture = null;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                if (step != null) RenderTexture.ReleaseTemporary(step);
                if (final != null) RenderTexture.ReleaseTemporary(final);
                if (texture != null) Destroy(texture);
            }
        }
    }
}
