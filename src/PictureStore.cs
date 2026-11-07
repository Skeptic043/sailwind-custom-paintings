using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CustomPaintings
{
    [DataContract]
    internal sealed class PictureRecord
    {
        [DataMember] public int InstanceId;
        [DataMember] public int PrefabIndex;
        [DataMember] public string Path;
        // Omitted when default, so plain Fill choices on black stay short in the save.
        [DataMember(EmitDefaultValue = false)] public string Mode;
        [DataMember(EmitDefaultValue = false)] public string Background;

        // An unknown mode, for example from a newer version, falls back to Fill.
        public FitMode FitMode => Mode != null && Enum.IsDefined(typeof(FitMode), Mode)
            ? (FitMode)Enum.Parse(typeof(FitMode), Mode) : FitMode.Fill;
        public string BackgroundColor => Background ?? DefaultBackground;
        public const string DefaultBackground = "#000000";
    }

    [DataContract]
    internal sealed class PictureSaveDocument
    {
        [DataMember(IsRequired = true)] public int Schema = 1;
        [DataMember(IsRequired = true)] public List<PictureRecord> Pictures = new List<PictureRecord>();
    }

    // Which picture each painting shows, stored as one string in the native mod-data dictionary,
    // so loading a save never needs this assembly. Without the mod, paintings keep their own art.
    // Records for paintings that are not instantiated, such as cached boat items, are kept
    // because they are the only copy.
    internal sealed class PictureStore
    {
        // The plugin GUID, repeated here so the logic tests can compile the store without BepInEx.
        public const string Key = "com.skeptic043.sailwind.custompaintings";
        private readonly Dictionary<int, PictureRecord> records = new Dictionary<int, PictureRecord>();

        public int Count => records.Count;
        // False after unreadable data, so saving leaves the original string untouched.
        public bool Writable { get; private set; } = true;
        public string Problem { get; private set; } = "";

        public void Reset()
        {
            records.Clear();
            Writable = true;
            Problem = "";
        }

        public bool Load(string json)
        {
            Reset();
            if (string.IsNullOrEmpty(json)) return true;
            try
            {
                PictureSaveDocument document;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    document = (PictureSaveDocument)new DataContractJsonSerializer(typeof(PictureSaveDocument)).ReadObject(stream);
                if (document == null || document.Schema != 1 || document.Pictures == null)
                    throw new SerializationException("unsupported picture save schema");
                foreach (PictureRecord record in document.Pictures)
                {
                    if (record == null || record.InstanceId == 0 || string.IsNullOrEmpty(record.Path))
                        throw new SerializationException("invalid picture record");
                    if (records.ContainsKey(record.InstanceId)) throw new SerializationException("duplicate picture identity");
                    records.Add(record.InstanceId, record);
                }
                return true;
            }
            catch (Exception ex) when (ex is SerializationException || ex is ArgumentException ||
                ex is System.Xml.XmlException || ex is FormatException || ex is InvalidCastException)
            {
                records.Clear();
                Writable = false;
                Problem = ex.Message;
                return false;
            }
        }

        public string Serialize()
        {
            var document = new PictureSaveDocument();
            document.Pictures.AddRange(records.Values);
            document.Pictures.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(PictureSaveDocument)).WriteObject(stream, document);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        // A different template under the same ID is not this painting's record.
        public bool TryGet(int instanceId, int prefabIndex, out PictureRecord record)
        {
            if (records.TryGetValue(instanceId, out record) && record.PrefabIndex == prefabIndex) return true;
            record = null;
            return false;
        }

        // Choosing a picture is the player's decision to replace unreadable saved data, which
        // otherwise would silently keep every new choice out of the save.
        public void Set(int instanceId, int prefabIndex, string path, FitMode mode, string background)
        {
            Writable = true;
            records[instanceId] = new PictureRecord
            {
                InstanceId = instanceId, PrefabIndex = prefabIndex, Path = path,
                Mode = mode == FitMode.Fill ? null : mode.ToString(),
                Background = background == null || background == PictureRecord.DefaultBackground ? null : background,
            };
        }

        public void Remove(int instanceId)
        {
            Writable = true;
            records.Remove(instanceId);
        }
    }
}
