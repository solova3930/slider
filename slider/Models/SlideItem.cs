namespace slider.Models
{
    public class SlideItem
    {
        public string FileName { get; set; } = "";
        public string ImagePath { get; set; } = "";
        public int DurationSeconds { get; set; } = 5;
        public string TransitionEffect { get; set; } = "Затухание";
    }
}