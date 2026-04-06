using System.Collections.Generic;

namespace slider.Models
{

    public class PlaylistData

    {
        public string PlaylistName { get; set; } = "Новый плейлист";
        public string PlaylistPath { get; set; } = "";
        public bool AutoSaveEnabled { get; set; } = false;
        public int AutoSaveMinutes { get; set; } = 5;
        public List<PlaylistPeriod> Periods { get; set; } = new();
        public StreamSettings StreamSettings { get; set; } = new();
    }
}