using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomPaintings
{
    // The picture menu: a paper page in front of the player, built from the game's own menu
    // panel, buttons and font and placed the way the pause menu is. It browses one folder at a
    // time (subfolders first), pages through 20 entries, searches the open folder and sets how
    // the picture sits in its frame. It is inactive, and costs nothing, while closed.
    internal sealed class PicturePicker : MonoBehaviour
    {
        private const float MaxButtonText = .0125f;
        private const int Columns = 5, Rows = 4, PerPage = Columns * Rows;
        private const float CellPitch = 1.0f, ThumbWidth = .88f, ThumbHeight = .44f;
        // The parchment size in page units. The page is scaled to the camera view when it opens.
        private const float PaperWidth = 6.2f, PaperHeight = 4.4f, PaperScreenHeight = 1.07f, PaperScreenWidth = 1.05f;
        // The parchment's visible sheet is about 85% of its mesh outline, so these place the sheet
        // itself at about 90% of the view height.
        // Camera to page distance: the holder is 1.3 ahead and the page 2.4 further at half scale.
        private const float PageDistance = 1.3f + 2.4f * .5f;
        private static readonly float[] RowY = { .86f, .24f, -.38f, -1.00f };
        private static readonly Color Ink = new Color(.06f, .035f, .02f, 1f);
        private static readonly Color MutedInk = new Color(.27f, .21f, .17f, 1f);
        private static readonly Color MarkInk = new Color(.45f, .08f, .03f, 1f);
        private static readonly Color Placeholder = new Color(.85f, .80f, .70f, 1f);
        private static readonly FieldInfo ChoiceUiField = AccessTools.Field(typeof(StartMenu), "chooseIslandUI");
        private static readonly string[] Swatches =
            { "#000000", "#FFFFFF", "#E8DCC0", "#7A7A7A", "#2B2B2B", "#1F2F4A", "#5A1E1E", "#3E2A1A" };

        private sealed class Cell
        {
            public MenuButton Folder;
            public MenuButton Thumb;
            public Material ThumbMaterial;
            public TextMesh Caption, Status;
            public LibraryEntry Entry;
            public bool HasEntry, WaitingForThumbnail;
        }

        private GameObject holder;
        private Transform root;
        private GameObject buttonTemplate;
        private TextMesh titleTemplate;
        private TextMesh fontTemplate;
        private Camera uiCamera;
        private Shader flatShader;
        private Material flatFallback;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly Dictionary<MenuButton, float> buttonWidths = new Dictionary<MenuButton, float>();
        private readonly Cell[] cells = new Cell[PerPage];
        private TextMesh pathText, pageText, messageText, borderLabel;
        private MenuButton backButton, searchButton, clearButton, previousButton, nextButton;
        private MenuButton[] modeButtons;
        private MenuButton[] swatchButtons;

        private PaintingPicture target;
        private List<string> roots = new List<string>();
        private int rootIndex = -1;
        private string folder;
        private List<LibraryEntry> listing = new List<LibraryEntry>();
        private List<LibraryEntry> shown = new List<LibraryEntry>();
        private string problem;
        private string query = "";
        private bool searchFocused;
        private int page;
        private FitMode mode;
        private string background;
        private readonly List<string> wanted = new List<string>();

        internal static PicturePicker Instance { get; private set; }
        public bool IsOpen => enabled;
        public bool SearchFocused => enabled && searchFocused;

        private void Awake()
        {
            Instance = this;
            enabled = false;
        }

        public void Open(PaintingPicture painting)
        {
            if (!EnsureBuilt()) return;
            target = painting;
            mode = painting.Mode;
            background = painting.Background;
            var configured = PictureLibrary.ParseFolders(Plugin.PictureFolders);
            if (!SameFolders(configured, roots)) { roots = configured; rootIndex = -1; folder = null; page = 0; }
            // The last folder is reopened while it still exists inside its configured folder.
            if (rootIndex >= 0 && !Inside(folder, roots[rootIndex])) folder = roots[rootIndex];
            query = "";
            searchFocused = false;
            LoadListing(keepPage: true);

            // The pause menu's placement (StartMenu.MoveMenuToPlayer), at its scale.
            bool vr = Settings.VRLookEnabled && Refs.observerMirror != null;
            var anchor = vr ? Refs.observerMirror.transform : Camera.main != null ? Camera.main.transform : null;
            holder.transform.SetParent(anchor, false);
            holder.transform.localPosition = vr ? new Vector3(0f, 0f, 1.3f) : new Vector3(0f, -1.1f, 1.3f);
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one * .5f;
            // Level with the eye, and as large as the view allows, so any field of view or
            // aspect ratio shows the whole parchment.
            root.localPosition = new Vector3(0f, vr ? 0f : 2.2f, 2.4f);
            var view = uiCamera != null ? uiCamera : Camera.main;
            float fov = view != null ? view.fieldOfView : 60f, aspect = view != null ? view.aspect : 16f / 9f;
            float visibleHeight = 2f * PageDistance * Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            float fit = Mathf.Min(PaperScreenHeight * visibleHeight / (PaperHeight * .5f),
                PaperScreenWidth * visibleHeight * aspect / (PaperWidth * .5f));
            root.localScale = Vector3.one * fit;
            holder.SetActive(true);
            MouseLook.ToggleMouseLookAndCursor(false);
            Refs.SetPlayerControl(false);
            // As in the pause menu, this stops the menu pointer from dropping a held item when a
            // button is clicked.
            GameState.wasInSettingsMenu = true;
            enabled = true;
            Refresh();
        }

        public void Close()
        {
            if (!enabled) return;
            enabled = false;
            target = null;
            searchFocused = false;
            if (holder != null) holder.SetActive(false);
            PictureTextures.Instance?.ClearThumbnails();
            MouseLook.ToggleMouseLookAndCursor(true);
            Refs.SetPlayerControl(true);
            StartCoroutine(ClearSettingsFlag());
        }

        private static IEnumerator ClearSettingsFlag()
        {
            yield return new WaitForEndOfFrame();
            GameState.wasInSettingsMenu = false;
        }

        public void Blur()
        {
            searchFocused = false;
            Refresh();
        }

        private void Update()
        {
            // The painting was destroyed, for example by loading a save.
            if (target == null || GameState.currentlyLoading || holder == null) { Close(); return; }
            if (searchFocused) ReadSearchInput();
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (wheel > 0f) ChangePage(-1);
            else if (wheel < 0f) ChangePage(1);
            // Thumbnails arrive over several frames.
            foreach (var cell in cells)
                if (cell.WaitingForThumbnail) ShowThumbnail(cell);
        }

        private void ReadSearchInput()
        {
            string before = query;
            bool wasFocused = searchFocused;
            foreach (char character in Input.inputString)
            {
                if (character == '\b') { if (query.Length > 0) query = query.Substring(0, query.Length - 1); }
                else if (character == '\n' || character == '\r') searchFocused = false;
                else if (!char.IsControl(character) && query.Length < 40) query += character;
            }
            if (Input.GetKeyDown(KeyCode.Delete)) query = "";
            if (query != before) { page = 0; Filter(); }
            if (query != before || searchFocused != wasFocused) Refresh();
        }

        // Navigation

        private void LoadListing(bool keepPage = false)
        {
            problem = null;
            if (rootIndex < 0)
            {
                listing = new List<LibraryEntry>();
                foreach (string path in roots) listing.Add(new LibraryEntry { Path = path, IsFolder = true });
            }
            else listing = PictureLibrary.List(folder, out problem);
            if (!keepPage) page = 0;
            Filter();
        }

        private void Filter()
        {
            shown = PictureLibrary.Filter(listing, query);
            page = Mathf.Clamp(page, 0, PictureLibrary.PageCount(shown.Count, PerPage) - 1);
        }

        private void Enter(LibraryEntry entry)
        {
            if (rootIndex < 0) rootIndex = roots.IndexOf(entry.Path);
            folder = entry.Path;
            query = "";
            searchFocused = false;
            LoadListing();
            Refresh();
        }

        private void Back()
        {
            if (rootIndex < 0) return;
            if (SamePath(folder, roots[rootIndex]))
            {
                rootIndex = -1;
                folder = null;
            }
            else folder = Path.GetDirectoryName(Normalize(folder));
            query = "";
            searchFocused = false;
            LoadListing();
            Refresh();
        }

        private bool AtTop => rootIndex < 0;

        private static string Normalize(string path) => path?.TrimEnd('\\', '/');

        private static bool SamePath(string a, string b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        private static bool Inside(string path, string rootPath) =>
            path != null && Directory.Exists(path) &&
            Normalize(path).StartsWith(Normalize(rootPath), StringComparison.OrdinalIgnoreCase);

        private static bool SameFolders(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private string PathLabel()
        {
            if (rootIndex < 0) return "Picture folders";
            string rootPath = Normalize(roots[rootIndex]);
            string name = Path.GetFileName(rootPath);
            if (string.IsNullOrEmpty(name)) name = rootPath;
            string current = Normalize(folder);
            string rest = current.Length > rootPath.Length ? current.Substring(rootPath.Length).Trim('\\', '/') : "";
            if (rest.Length == 0) return name;
            return name + " / " + string.Join(" / ", rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ChangePage(int direction)
        {
            int next = Mathf.Clamp(page + direction, 0, PictureLibrary.PageCount(shown.Count, PerPage) - 1);
            if (next == page) return;
            page = next;
            Refresh();
        }

        // Actions

        private void OnCell(int index)
        {
            var cell = cells[index];
            if (target == null || !cell.HasEntry) return;
            searchFocused = false;
            if (cell.Entry.IsFolder) { Enter(cell.Entry); return; }
            target.Choose(cell.Entry.Path, mode, background);
            Close();
        }

        // Applies straight away to a painting already showing a picture, otherwise to the next one chosen.
        private void SetMode(FitMode next)
        {
            mode = next;
            target.SetFit(mode, background);
            Refresh();
        }

        private void SetBackground(string color)
        {
            background = color;
            target.SetFit(mode, background);
            Refresh();
        }

        // Display

        private void Refresh()
        {
            if (holder == null || target == null) return;
            pathText.text = Trim(PathLabel(), 70);
            SetVisible(backButton, !AtTop);
            SetText(searchButton, searchFocused ? query + "_" : query.Length > 0 ? query : "Search this folder",
                searchFocused || query.Length > 0 ? Ink : MutedInk);
            SetVisible(clearButton, query.Length > 0);

            string message = null;
            if (roots.Count == 0) message = "Add picture folders to the Picture folders setting\nin the config file or Configuration Manager.";
            else if (shown.Count == 0) message = problem ?? (query.Length > 0 ? "Nothing here matches \"" + query + "\"." : "No folders or PNG or JPG pictures here.");
            messageText.text = message ?? "";

            wanted.Clear();
            for (int i = 0; i < PerPage; i++)
            {
                int index = page * PerPage + i;
                var cell = cells[i];
                cell.HasEntry = message == null && index < shown.Count;
                cell.WaitingForThumbnail = false;
                if (!cell.HasEntry)
                {
                    SetVisible(cell.Folder, false);
                    cell.Thumb.gameObject.SetActive(false);
                    cell.Caption.text = cell.Status.text = "";
                    continue;
                }
                cell.Entry = shown[index];
                if (cell.Entry.IsFolder)
                {
                    SetVisible(cell.Folder, true);
                    cell.Thumb.gameObject.SetActive(false);
                    SetText(cell.Folder, Wrap(cell.Entry.Name, 13, 3), Ink);
                    cell.Caption.text = "folder";
                    cell.Caption.color = MutedInk;
                    cell.Status.text = "";
                }
                else
                {
                    SetVisible(cell.Folder, false);
                    cell.Caption.text = Trim(cell.Entry.Name, 22);
                    cell.Caption.color = SamePath(cell.Entry.Path, target.Path) ? MarkInk : Ink;
                    ShowThumbnail(cell);
                    if (cell.WaitingForThumbnail) wanted.Add(cell.Entry.Path);
                }
            }
            PictureTextures.Instance?.WantThumbnails(wanted);

            int pages = PictureLibrary.PageCount(shown.Count, PerPage);
            pageText.text = message == null && pages > 1 ? "Page " + (page + 1) + " of " + pages : "";
            SetVisible(previousButton, message == null && page > 0);
            SetVisible(nextButton, message == null && page < pages - 1);

            for (int i = 0; i < modeButtons.Length; i++)
                SetText(modeButtons[i], ((FitMode)i).ToString(), (FitMode)i == mode ? MarkInk : Ink);
            bool fit = mode == FitMode.Fit;
            borderLabel.text = fit ? "Border" : "";
            for (int i = 0; i < swatchButtons.Length; i++)
            {
                swatchButtons[i].gameObject.SetActive(fit);
                float size = Swatches[i] == background ? .22f : .17f;
                swatchButtons[i].transform.localScale = new Vector3(size, size, 1f);
            }
        }

        private void ShowThumbnail(Cell cell)
        {
            var textures = PictureTextures.Instance;
            var thumbnail = textures?.Thumbnail(cell.Entry.Path);
            if (thumbnail != null)
            {
                cell.ThumbMaterial.mainTexture = thumbnail;
                cell.ThumbMaterial.color = Color.white;
                float aspect = thumbnail.width / (float)thumbnail.height;
                float width = ThumbWidth, height = ThumbWidth / aspect;
                if (height > ThumbHeight) { height = ThumbHeight; width = ThumbHeight * aspect; }
                cell.Thumb.transform.localScale = new Vector3(width, height, 1f);
                cell.Thumb.gameObject.SetActive(true);
                cell.Status.text = "";
                cell.WaitingForThumbnail = false;
                return;
            }
            bool failed = textures != null && textures.ThumbnailFailed(cell.Entry.Path);
            // A plain card stays clickable while the thumbnail loads.
            cell.ThumbMaterial.mainTexture = Texture2D.whiteTexture;
            cell.ThumbMaterial.color = Placeholder;
            cell.Thumb.transform.localScale = new Vector3(ThumbWidth * .8f, ThumbHeight * .8f, 1f);
            cell.Thumb.gameObject.SetActive(true);
            cell.Status.text = failed ? "can't read" : "loading";
            cell.WaitingForThumbnail = !failed;
        }

        private static string Trim(string value, int maximum) =>
            value.Length <= maximum ? value : value.Substring(0, maximum - 3) + "...";

        private static string Wrap(string value, int width, int lines)
        {
            var result = new List<string>();
            string rest = value;
            while (rest.Length > 0 && result.Count < lines)
            {
                if (rest.Length <= width) { result.Add(rest); rest = ""; break; }
                int cut = rest.LastIndexOf(' ', width);
                if (cut <= 0) cut = width;
                result.Add(rest.Substring(0, cut).TrimEnd());
                rest = rest.Substring(cut).TrimStart();
            }
            if (rest.Length > 0) result[result.Count - 1] = Trim(result[result.Count - 1] + " " + rest, width);
            return string.Join("\n", result.ToArray());
        }

        // Construction

        private bool EnsureBuilt()
        {
            if (holder != null) return true;
            try
            {
                Build();
                return true;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError("Custom Paintings could not build its picture menu: " + exception);
                if (holder != null) Destroy(holder);
                holder = null;
                return false;
            }
        }

        private void Build()
        {
            var menu = FindObjectOfType<StartMenu>();
            var choice = menu != null ? ChoiceUiField?.GetValue(menu) as GameObject : null;
            if (choice == null) throw new InvalidOperationException("Sailwind's menu panel was not found.");
            var templateRoot = choice.transform;
            var confirm = templateRoot.Find("button confirm");
            var title = templateRoot.Find("text title")?.GetComponent<TextMesh>();
            var panel = templateRoot.Find("bg");
            fontTemplate = confirm != null ? confirm.Find("text")?.GetComponent<TextMesh>() : null;
            if (confirm == null || title == null || panel == null || fontTemplate == null)
                throw new InvalidOperationException("Sailwind's menu button, text or panel templates were not found.");
            buttonTemplate = confirm.gameObject;
            titleTemplate = title;
            // The menu pointer raycasts through this camera, which also draws the menu layer.
            uiCamera = GameObject.Find("UI camera")?.GetComponent<Camera>();
            flatShader = Shader.Find("Sprites/Default");
            flatFallback = panel.GetComponent<Renderer>()?.sharedMaterial;

            // Built inactive so cloned native components do not wake before they are replaced.
            holder = new GameObject("Custom Paintings picture menu");
            holder.SetActive(false);
            holder.layer = choice.layer;
            root = new GameObject("page").transform;
            root.gameObject.layer = choice.layer;
            root.SetParent(holder.transform, false);
            // The same frame as the native menu panels, raised to eye level.

            var sheet = Instantiate(panel.gameObject, root, false);
            sheet.name = "paper";
            sheet.transform.localPosition = new Vector3(0f, 0f, .06f);
            sheet.transform.localRotation = panel.localRotation;
            // The parchment mesh is rotated: its local Y is horizontal and its local Z vertical.
            // Mesh bounds remain available although the mesh itself is not readable.
            var mesh = sheet.GetComponent<MeshFilter>()?.sharedMesh;
            var size = mesh != null ? mesh.bounds.size : Vector3.one;
            sheet.transform.localScale = new Vector3(panel.localScale.x,
                PaperWidth / Mathf.Max(.01f, size.y), PaperHeight / Mathf.Max(.01f, size.z));

            Label("Choose a Picture", 0f, 1.60f, .017f, Ink);
            pathText = Label("", 0f, 1.43f, .0095f, Ink);
            backButton = Button("Back", -2.25f, 1.27f, .50f, .17f, Back);
            searchButton = Button("Search this folder", .05f, 1.27f, 3.20f, .17f, () => { searchFocused = true; Refresh(); });
            clearButton = Button("Clear", 2.30f, 1.27f, .48f, .17f, () => { query = ""; page = 0; Filter(); Refresh(); });
            messageText = Label("", 0f, 0f, .0100f, Ink);

            for (int i = 0; i < PerPage; i++)
            {
                float x = (i % Columns - (Columns - 1) / 2f) * CellPitch;
                float y = RowY[i / Columns];
                int index = i;
                var cell = new Cell();
                cell.Folder = Button("Folder", x, y, ThumbWidth, ThumbHeight, () => OnCell(index));
                cell.Thumb = Flat("thumbnail " + i, x, y, .03f, out cell.ThumbMaterial);
                cell.Thumb.Clicked = () => OnCell(index);
                cell.Caption = Label("", x, y - ThumbHeight / 2f - .075f, .0088f, Ink);
                cell.Status = Label("", x, y, .0085f, Ink);
                cell.Status.transform.localPosition += new Vector3(0f, 0f, -.01f);
                cells[i] = cell;
            }

            modeButtons = new MenuButton[3];
            float[] modeX = { -2.30f, -1.82f, -1.26f };
            float[] modeWidth = { .44f, .44f, .58f };
            for (int i = 0; i < modeButtons.Length; i++)
            {
                var fitMode = (FitMode)i;
                modeButtons[i] = Button(fitMode.ToString(), modeX[i], -1.48f, modeWidth[i], .15f, () => SetMode(fitMode));
            }
            borderLabel = Label("", -.72f, -1.48f, .0090f, Ink);
            swatchButtons = new MenuButton[Swatches.Length];
            for (int i = 0; i < Swatches.Length; i++)
            {
                string color = Swatches[i];
                swatchButtons[i] = Flat("border " + color, -.38f + i * .25f, -1.48f, .03f, out var material);
                PictureFit.ParseColor(color, out float r, out float g, out float b);
                material.color = new Color(r, g, b, 1f);
                swatchButtons[i].Clicked = () => SetBackground(color);
            }

            Button("Original Art", -2.15f, -1.72f, .78f, .17f, () => { target.ChooseOriginal(); Close(); });
            previousButton = Button("<", -.60f, -1.72f, .27f, .17f, () => ChangePage(-1));
            pageText = Label("", 0f, -1.72f, .0095f, Ink);
            nextButton = Button(">", .60f, -1.72f, .27f, .17f, () => ChangePage(1));
            Button("Close", 2.20f, -1.72f, .60f, .17f, Close);
        }

        private TextMesh Label(string value, float x, float y, float scale, Color color)
        {
            var label = Instantiate(titleTemplate.gameObject, root, false);
            label.name = "label";
            label.transform.localPosition = new Vector3(x, y, titleTemplate.transform.localPosition.z);
            label.transform.localRotation = titleTemplate.transform.localRotation;
            label.transform.localScale = Vector3.one * scale;
            var text = label.GetComponent<TextMesh>();
            // The title template uses the italic face; native buttons use the bold one.
            text.font = fontTemplate.font;
            text.fontStyle = fontTemplate.fontStyle;
            text.fontSize = fontTemplate.fontSize;
            text.characterSize = fontTemplate.characterSize;
            text.GetComponent<Renderer>().sharedMaterial = fontTemplate.GetComponent<Renderer>().sharedMaterial;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.text = value;
            return text;
        }

        private MenuButton Button(string label, float x, float y, float width, float height, Action onClick)
        {
            var visual = Instantiate(buttonTemplate, root, false);
            visual.name = "button " + label;
            visual.transform.localPosition = new Vector3(x, y, buttonTemplate.transform.localPosition.z);
            visual.transform.localRotation = buttonTemplate.transform.localRotation;
            visual.transform.localScale = Vector3.one;
            var native = visual.GetComponentInChildren<StartMenuButton>(true);
            if (native == null) throw new InvalidOperationException("The cloned menu button has no pointer target.");
            var surface = native.transform;
            DestroyImmediate(native);
            // Scale the surface, not the root, so the lettering keeps its proportions.
            var mesh = surface.GetComponent<MeshFilter>()?.sharedMesh;
            var meshSize = mesh != null ? mesh.bounds.size : Vector3.one;
            surface.localScale = new Vector3(width / Mathf.Max(.01f, meshSize.x), height / Mathf.Max(.01f, meshSize.y), surface.localScale.z);
            var texts = visual.GetComponentsInChildren<TextMesh>(true);
            for (int i = 1; i < texts.Length; i++) texts[i].gameObject.SetActive(false);
            var button = surface.gameObject.AddComponent<MenuButton>();
            button.Clicked = () =>
            {
                // Clicking anything else ends typing in the search field.
                if (button != searchButton) searchFocused = false;
                onClick();
            };
            button.lookText = "";
            button.description = "";
            buttonWidths[button] = width;
            SetText(button, label, Ink);
            return button;
        }

        // A flat coloured or textured target, used for thumbnails and border swatches.
        private MenuButton Flat(string name, float x, float y, float z, out Material material)
        {
            var go = new GameObject(name);
            go.layer = root.gameObject.layer;
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(x, y, z);
            go.AddComponent<MeshFilter>().sharedMesh = Quad.Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            material = flatShader != null ? new Material(flatShader) : new Material(flatFallback);
            material.mainTexture = Texture2D.whiteTexture;
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            ownedMaterials.Add(material);
            renderer.sharedMaterial = material;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 1f, .02f);
            return go.AddComponent<MenuButton>();
        }

        private static TextMesh ButtonText(MenuButton button) =>
            button != null ? button.transform.parent.GetComponentInChildren<TextMesh>(true) : null;

        // Native-size lettering, shrunk for long labels and narrow buttons.
        private void SetText(MenuButton button, string value, Color color)
        {
            var text = ButtonText(button);
            if (text == null) return;
            text.text = value;
            text.color = color;
            int longest = 1;
            foreach (string line in value.Split('\n')) longest = Math.Max(longest, line.Length);
            buttonWidths.TryGetValue(button, out float width);
            // A glyph is about 3.85 times the text scale wide in page units; keep a margin.
            text.transform.localScale = Vector3.one * Mathf.Min(MaxButtonText, width * .85f / (3.85f * longest));
        }

        private static void SetVisible(MenuButton button, bool visible)
        {
            if (button != null) button.transform.parent.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
            foreach (var material in ownedMaterials) if (material != null) Destroy(material);
            if (holder != null) Destroy(holder);
        }
    }
}
