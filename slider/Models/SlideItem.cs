namespace slider.Models
{
    public enum MediaType
    {
        Image,
        Video
    }

    public class SlideItem
    {
        public string FileName { get; set; } = "";
        public string Path { get; set; } = "";

        public MediaType Type { get; set; } = MediaType.Image;

        public int DurationSeconds { get; set; } = 5;
        public string TransitionEffect { get; set; } = "Затухание";

        public bool PlayFullVideo { get; set; } = true;
        public double StartSeconds { get; set; } = 0;
        public double EndSeconds { get; set; } = 0;
    }
}