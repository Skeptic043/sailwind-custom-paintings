using System;
using System.Collections.Generic;
using System.IO;

namespace CustomPaintings
{
    internal struct LibraryEntry
    {
        public string Path;
        public bool IsFolder;
        public string Name => IsFolder ? System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) : System.IO.Path.GetFileNameWithoutExtension(Path);
    }

    // Reads the configured picture folders one level at a time: a folder's subfolders, then its
    // pictures. Nothing below the open folder is read until the player opens it.
    internal static class PictureLibrary
    {
        public static List<string> ParseFolders(string setting)
        {
            var folders = new List<string>();
            if (string.IsNullOrEmpty(setting)) return folders;
            foreach (string part in setting.Split('|'))
            {
                string folder = part.Trim().Trim('"').Trim();
                if (folder.Length > 0 && !folders.Contains(folder)) folders.Add(folder);
            }
            return folders;
        }

        public static bool IsPicture(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        // Subfolders first, then pictures, each in natural name order.
        public static List<LibraryEntry> List(string folder, out string problem)
        {
            problem = null;
            var entries = new List<LibraryEntry>();
            if (!Directory.Exists(folder))
            {
                problem = "Folder not found: " + folder;
                return entries;
            }
            try
            {
                var folders = new List<string>(Directory.GetDirectories(folder));
                var pictures = new List<string>();
                foreach (string file in Directory.GetFiles(folder))
                    if (IsPicture(file)) pictures.Add(file);
                folders.Sort(NaturalCompare);
                pictures.Sort(NaturalCompare);
                foreach (string path in folders) entries.Add(new LibraryEntry { Path = path, IsFolder = true });
                foreach (string path in pictures) entries.Add(new LibraryEntry { Path = path });
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                problem = "This folder could not be read: " + ex.Message;
            }
            return entries;
        }

        // Search covers only the open folder's own subfolders and pictures, by name.
        public static List<LibraryEntry> Filter(List<LibraryEntry> entries, string query)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query)) return entries;
            var matches = new List<LibraryEntry>();
            foreach (var entry in entries)
                if (entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(entry);
            return matches;
        }

        public static int PageCount(int count, int perPage) => Math.Max(1, (count + perPage - 1) / perPage);

        // Compares names the way people number files: "photo 2" comes before "photo 10".
        public static int NaturalCompare(string a, string b)
        {
            a = Path.GetFileName((a ?? "").TrimEnd('\\', '/'));
            b = Path.GetFileName((b ?? "").TrimEnd('\\', '/'));
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int startA = i, startB = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string numberA = a.Substring(startA, i - startA).TrimStart('0');
                    string numberB = b.Substring(startB, j - startB).TrimStart('0');
                    if (numberA.Length != numberB.Length) return numberA.Length.CompareTo(numberB.Length);
                    int digits = string.CompareOrdinal(numberA, numberB);
                    if (digits != 0) return digits;
                }
                else
                {
                    int letters = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (letters != 0) return letters;
                    i++;
                    j++;
                }
            }
            int length = (a.Length - i).CompareTo(b.Length - j);
            return length != 0 ? length : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
