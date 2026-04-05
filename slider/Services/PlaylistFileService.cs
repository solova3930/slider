using slider.Models;
using System.IO;
using System.Text.Json;

namespace slider.Services
{
    public static class PlaylistFileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static void Save(string filePath, PlaylistData playlist)
        {
            string json = JsonSerializer.Serialize(playlist, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        public static PlaylistData Load(string filePath)
        {
            string json = File.ReadAllText(filePath);
            var playlist = JsonSerializer.Deserialize<PlaylistData>(json, JsonOptions);

            return playlist ?? new PlaylistData();
        }
    }
}