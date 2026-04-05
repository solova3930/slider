using System;
using System.Collections.Generic;

namespace slider.Models
{
    public class PlaylistPeriod
    {
        public string Name { get; set; } = "";
        public DateTime StartDateTime { get; set; } = DateTime.Today;
        public DateTime EndDateTime { get; set; } = DateTime.Today.AddDays(1);
        public List<SlideItem> Slides { get; set; } = new();
    }
}