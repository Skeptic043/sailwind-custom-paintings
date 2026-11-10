using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CustomPaintings
{
    [BepInPlugin(Id, "Custom Paintings", "0.1.10")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = PictureStore.Key;
        private const string ChangePrompt = "change picture";
        private static ConfigEntry<string> pictureFolders;
        private static ConfigEntry<int> maxSize;
        private static ConfigEntry<bool> compress;
        private static ConfigEntry<KeyboardShortcut> screenshotKey;
        private static ConfigEntry<string> screenshotFolder;
        private static ConfigEntry<bool> screenshotNotice;
        private Harmony harmony;

        internal static ManualLogSource Log { get; private set; }
        internal static PictureStore Store { get; } = new PictureStore();

        // Read on every use so Configuration Manager edits apply immediately.
        internal static string PictureFolders => pictureFolders.Value;
        internal static int MaxSize => maxSize.Value;
        internal static bool Compress => compress.Value;
        internal static string ScreenshotFolder
        {
            get
            {
                string folder = screenshotFolder.Value?.Trim().Trim('"').Trim();
                return string.IsNullOrEmpty(folder) ? Path.Combine(Paths.BepInExRootPath, "Custom Paintings Screenshots") : folder;
            }
        }

        private void Awake()
        {
            Log = Logger;
            pictureFolders = Config.Bind("Pictures", "Picture folders", "",
                "Folders to choose pictures from, separated by |. Each one is listed when the menu opens, and its subfolders can be browsed. PNG and JPG are supported.");
            maxSize = Config.Bind("Pictures", "Maximum size", 1024, new ConfigDescription(
                "Pictures larger than this on their longer side are shrunk to it. Smaller pictures keep their size. 1024 uses a quarter of the memory of 2048. 2048 matches the game's own paintings and looks sharper up close.",
                new AcceptableValueList<int>(1024, 2048, 4096)));
            compress = Config.Bind("Pictures", "Compress pictures", true,
                "Store pictures compressed in video memory, using about a sixth of the memory with a slight loss of quality.");
            screenshotKey = Config.Bind("Screenshots", "Screenshot key", new KeyboardShortcut(KeyCode.F2),
                "Saves a screenshot of the game as a PNG in the screenshot folder. The picture menu lists that folder.");
            screenshotFolder = Config.Bind("Screenshots", "Screenshot folder", "",
                "Folder screenshots are saved in. Blank uses BepInEx/Custom Paintings Screenshots.");
            screenshotNotice = Config.Bind("Screenshots", "Screenshot notification", true,
                "Shows the game's notification scroll saying \"Screenshot taken\" after each screenshot.");
            try
            {
                gameObject.AddComponent<PictureTextures>();
                gameObject.AddComponent<PicturePicker>();
                maxSize.SettingChanged += (_, __) => PictureTextures.Instance?.ReloadAll();
                compress.SettingChanged += (_, __) => PictureTextures.Instance?.ReloadAll();
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                Logger.LogInfo("Custom Paintings loaded.");
            }
            catch (Exception exception)
            {
                // A changed Sailwind method can fail after earlier patches were installed.
                enabled = false;
                try { harmony?.UnpatchSelf(); }
                catch (Exception unpatchException)
                {
                    Logger.LogError("Custom Paintings could not remove its partial patches: " + unpatchException);
                }
                Logger.LogError("Custom Paintings disabled after initialization failed. " +
                    "Check game and mod compatibility: " + exception);
            }
        }

        // Not while the picture menu is open, where the key could be part of a search.
        private void Update()
        {
            if (!screenshotKey.Value.IsDown()) return;
            if (PicturePicker.Instance != null && PicturePicker.Instance.IsOpen) return;
            bool taken = false;
            Guard("screenshot", () => { TakeScreenshot(); taken = true; });
            if (taken && screenshotNotice.Value) StartCoroutine(NoticeAfterCapture());
        }

        // The capture happens at the end of this frame, so the scroll waits a frame and stays out of the picture.
        private static System.Collections.IEnumerator NoticeAfterCapture()
        {
            yield return null;
            var notifications = NotificationUi.instance;
            if (notifications != null) Guard("screenshot notification", () => notifications.ShowNotification("Screenshot taken"));
        }

        // Unity writes the file at the end of the frame, with whatever is on screen.
        private static void TakeScreenshot()
        {
            string folder = ScreenshotFolder;
            Directory.CreateDirectory(folder);
            string name = "Sailwind " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string path = Path.Combine(folder, name + ".png");
            for (int copy = 2; File.Exists(path); copy++) path = Path.Combine(folder, $"{name} ({copy}).png");
            ScreenCapture.CaptureScreenshot(path);
            Log.LogInfo("Screenshot saved to " + path);
        }

        private static void Guard(string patch, Action action)
        {
            try { action(); }
            catch (Exception exception) { Failed(patch, exception); }
        }

        // The pointer and look prompts run every frame, so they catch directly instead of
        // building a Guard closure on each call.
        private static void Failed(string patch, Exception exception) =>
            Log.LogError($"Custom Paintings {patch} failed: {exception}");

        private static PaintingPicture Picture(Component item) => item != null ? item.GetComponent<PaintingPicture>() : null;

        // Runs after native and cached-boat restoration have set the item's saved identity.
        // LoadModData has already run by then, so the saved picture is known.
        [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.OnLoad))]
        private static class AttachPatch
        {
            private static void Postfix(ShipItem __instance) => Guard("attach", () =>
            {
                if (PaintingPicture.IsPainting(__instance) && Picture(__instance) == null)
                    __instance.gameObject.AddComponent<PaintingPicture>().Init(__instance);
            });
        }

        // R on an owned painting, held or looked at with empty hands, opens the picker. Native
        // R does nothing for an owned painting. On shop stock it still buys the painting.
        [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.OnAltActivate), new Type[0])]
        private static class AltActivatePatch
        {
            private static bool Prefix(ShipItem __instance)
            {
                bool consumed = false;
                Guard("open picker", () =>
                {
                    var picture = Picture(__instance);
                    if (picture == null || !picture.CanChoose || PicturePicker.Instance == null || PicturePicker.Instance.IsOpen) return;
                    PicturePicker.Instance.Open(picture);
                    consumed = true;
                });
                return !consumed;
            }
        }

        // Escape closes the picker instead of opening the pause menu, as it does for native menus.
        [HarmonyPatch(typeof(StartMenu), "LateUpdate")]
        private static class EscapePatch
        {
            private static bool Prefix()
            {
                var picker = PicturePicker.Instance;
                if (picker == null || !picker.IsOpen) return true;
                if (!Input.GetKeyDown("escape") && !Input.GetKeyDown("f10") && !Input.GetKeyDown(KeyCode.JoystickButton6)) return true;
                // Escape first ends typing in the search field, then closes the menu.
                Guard("escape", () => { if (picker.SearchFocused) picker.Blur(); else picker.Close(); });
                return false;
            }
        }

        private static class PointerAccess
        {
            public static readonly AccessTools.FieldRef<GoPointer, RaycastHit> Hit =
                AccessTools.FieldRefAccess<GoPointer, RaycastHit>("hit");
            public static readonly AccessTools.FieldRef<GoPointer, GoPointerButton> PointedAt =
                AccessTools.FieldRefAccess<GoPointer, GoPointerButton>("pointedAtButton");
            public static readonly AccessTools.FieldRef<GoPointer, float> LookDistance =
                AccessTools.FieldRefAccess<GoPointer, float>("currentLookDistance");
        }

        // With empty hands the native raycast only targets nailed crates, bottles and beds, so a
        // nailed painting could not be looked at. Target it the same way, after the raycast has
        // stored this frame's hit.
        [HarmonyPatch(typeof(GoPointer), "DoRaycast")]
        private static class PointerTargetPatch
        {
            private static void Postfix(GoPointer __instance)
            {
                try { Target(__instance); }
                catch (Exception exception) { Failed("pointer target", exception); }
            }

            private static void Target(GoPointer __instance)
            {
                if (__instance.type == GoPointer.PointerType.crosshairMouse && GameState.inCursorMenu) return;
                if (PointerAccess.PointedAt(__instance) != null || __instance.GetHeldItem() != null) return;
                if (GameState.sleeping || GameState.inBed != null || BoatCamera.on) return;
                var hit = PointerAccess.Hit(__instance);
                var collider = hit.collider;
                if (collider == null) return;
                if (collider.CompareTag("ItemSubcollider")) collider = collider.transform.parent.GetComponent<Collider>();
                var item = collider != null ? collider.GetComponent<ShipItem>() : null;
                if (item == null || !item.nailed || item.unclickable || Picture(item) == null) return;
                // Targeted for R only. No Look call, so a nailed painting shows no hover outline.
                PointerAccess.PointedAt(__instance) = item;
                PointerAccess.LookDistance(__instance) = hit.distance;
            }
        }

        private static class LookUiAccess
        {
            public static readonly AccessTools.FieldRef<LookUI, TextMesh> ControlsText =
                AccessTools.FieldRefAccess<LookUI, TextMesh>("controlsText");
            public static readonly AccessTools.FieldRef<LookUI, GoPointer> Pointer =
                AccessTools.FieldRefAccess<LookUI, GoPointer>("pointer");
            public static readonly Action<LookUI> ShowRicon =
                AccessTools.MethodDelegate<Action<LookUI>>(AccessTools.Method(typeof(LookUI), "ShowRicon"));
            public static readonly Action<LookUI> HideIcons =
                AccessTools.MethodDelegate<Action<LookUI>>(AccessTools.Method(typeof(LookUI), "HideIcons"));
        }

        // Shows "change picture" beside the R icon, the way native items show their R action.
        // A nailed painting looked at with empty hands shows nothing at all, though R still works.
        [HarmonyPatch(typeof(LookUI), nameof(LookUI.ShowLookText))]
        private static class LookPromptPatch
        {
            private static void Postfix(LookUI __instance, GoPointerButton button)
            {
                try { Prompt(__instance, button); }
                catch (Exception exception) { Failed("look prompt", exception); }
            }

            private static void Prompt(LookUI __instance, GoPointerButton button)
            {
                if (button.IsStickyClicked()) return;
                var picture = Picture(button);
                if (picture == null || !picture.CanChoose) return;
                var pointer = LookUiAccess.Pointer(__instance);
                if (pointer == null || pointer.GetHeldItem() != null) return;
                if (((ShipItem)button).nailed)
                {
                    __instance.ClearText();
                    return;
                }
                if (!Settings.controlsTextEnabled) return;
                LookUiAccess.ShowRicon(__instance);
                var text = LookUiAccess.ControlsText(__instance);
                text.text = text.text.Split('\n')[0] + "\n" + ChangePrompt;
            }
        }

        [HarmonyPatch(typeof(LookUI), nameof(LookUI.ShowHoldText))]
        private static class HoldPromptPatch
        {
            private static void Postfix(LookUI __instance, PickupableItem item)
            {
                try
                {
                    var picture = Picture(item);
                    if (!Settings.controlsTextEnabled || picture == null || !picture.CanChoose) return;
                    LookUiAccess.ShowRicon(__instance);
                    LookUiAccess.ControlsText(__instance).text = ChangePrompt;
                }
                catch (Exception exception) { Failed("hold prompt", exception); }
            }
        }

        [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveModData))]
        private static class SavePatch
        {
            private static void Postfix() => Guard("save", () =>
            {
                if (!Store.Writable) return;
                if (Store.Count > 0) GameState.modData[PictureStore.Key] = Store.Serialize();
                else GameState.modData.Remove(PictureStore.Key);
            });
        }

        [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadGame))]
        private static class LoadGamePatch
        {
            private static void Prefix() => Guard("load reset", Reset);
        }

        [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadModData))]
        private static class LoadModDataPatch
        {
            private static void Postfix() => Guard("load", () =>
            {
                GameState.modData.TryGetValue(PictureStore.Key, out string json);
                if (!Store.Load(json))
                    Log.LogWarning("Saved pictures could not be read and were left unchanged in the save: " + Store.Problem);
            });
        }

        // GameState.modData is not cleared for a new game, so earlier pictures must not carry over.
        [HarmonyPatch(typeof(StartMenu), "StartNewGame")]
        private static class NewGamePatch
        {
            private static void Prefix() => Guard("new game reset", Reset);
        }

        private static void Reset()
        {
            PicturePicker.Instance?.Close();
            Store.Reset();
            // A save without mod data keeps the previous session's dictionary.
            GameState.modData.Remove(PictureStore.Key);
        }
    }
}
