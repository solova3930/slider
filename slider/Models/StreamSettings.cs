namespace slider.Models
{
    public class StreamSettings
    {
        public bool EnableStreaming { get; set; } = false;
        public string OutputUrl { get; set; } = "";
        public string Codec { get; set; } = "H264";
        public int BitrateKbps { get; set; } = 4000;
        public int Fps { get; set; } = 25;
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public string Preset { get; set; } = "medium";
        public bool LoopPlaylist { get; set; } = true;
    }
}