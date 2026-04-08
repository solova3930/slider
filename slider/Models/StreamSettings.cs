namespace slider.Models
{
    public class StreamSettings
    {
        public bool EnableStreaming { get; set; } = false;
        public string FfmpegPath { get; set; } = "ffmpeg.exe";
        public string OutputUrl { get; set; } = "";
        public string Codec { get; set; } = "H264";
        public string VideoCodec { get; set; } = "libx264";
        public string Format { get; set; } = "mpegts";
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int Fps { get; set; } = 25;
        public int BitrateKbps { get; set; } = 4000;
        public string Preset { get; set; } = "veryfast";
        public bool LoopPlaylist { get; set; } = false;
    }
}