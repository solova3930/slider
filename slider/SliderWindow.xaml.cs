using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace slider
{
    public partial class SliderWindow : Window
    {
        private List<string> images = new List<string>()
        {
            @"C:\Temp\1.jpg",
            @"C:\Temp\2.jpg",
            @"C:\Temp\3.jpg"
        };

        private int currentIndex = 0;
        private DispatcherTimer timer;

        public SliderWindow()
        {
            InitializeComponent();

            ShowImage();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            currentIndex++;

            if (currentIndex >= images.Count)
                currentIndex = 0;

            ShowImage();
        }

        private void ShowImage()
        {
            if (images.Count == 0)
                return;

            SlideImage.Source = new BitmapImage(new Uri(images[currentIndex]));
        }
    }
}