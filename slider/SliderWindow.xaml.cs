using slider.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace slider
{
    public partial class SliderWindow : Window
    {
        private readonly List<PlaylistPeriod> periods;
        private List<SlideItem> currentSlides = new();
        private int currentIndex = 0;
        private readonly DispatcherTimer slideTimer;
        private readonly DispatcherTimer periodCheckTimer;
        private string playlistFilePath = "";
        private DateTime lastFileWriteTime = DateTime.MinValue;
        private void CheckPlaylistFileChanges()
        {
            if (string.IsNullOrWhiteSpace(playlistFilePath))
                return;

            if (!File.Exists(playlistFilePath))
                return;

            DateTime writeTime = File.GetLastWriteTime(playlistFilePath);

            if (writeTime <= lastFileWriteTime)
                return;

            lastFileWriteTime = writeTime;

            try
            {
                var data = slider.Services.PlaylistFileService.Load(playlistFilePath);

                periods.Clear();

                if (data.Periods != null)
                {
                    foreach (var p in data.Periods)
                        periods.Add(p);
                }

                ReloadActiveSlides(true);
            }
            catch
            {
                // можно лог или игнор
            }
        }

        public SliderWindow(List<PlaylistPeriod> playlistPeriods, string filePath)
        {
            InitializeComponent();

            periods = playlistPeriods ?? new List<PlaylistPeriod>();
            playlistFilePath = filePath;

            slideTimer = new DispatcherTimer();
            slideTimer.Tick += SlideTimer_Tick;

            periodCheckTimer = new DispatcherTimer();
            periodCheckTimer.Interval = TimeSpan.FromSeconds(1);
            periodCheckTimer.Tick += PeriodCheckTimer_Tick;
            periodCheckTimer.Start();

            ReloadActiveSlides(true);
        }

        private void SlideTimer_Tick(object? sender, EventArgs e)
        {
            if (currentSlides.Count == 0)
            {
                slideTimer.Stop();
                return;
            }

            currentIndex++;

            if (currentIndex >= currentSlides.Count)
                currentIndex = 0;

            ShowCurrentSlide();
            RestartSlideTimer();
        }



        private void PeriodCheckTimer_Tick(object? sender, EventArgs e)
        {
            CheckPlaylistFileChanges();
            ReloadActiveSlides(forceReload: false);
        }


        private List<PlaylistPeriod> GetActivePeriods()
        {
            DateTime now = DateTime.Now;

            return periods
                .Where(p => now >= p.StartDateTime && now < p.EndDateTime)
                .OrderBy(p => p.StartDateTime)
                .ToList();
        }

        private void ReloadActiveSlides(bool forceReload)
        {
            List<PlaylistPeriod> activePeriods = GetActivePeriods();

            List<SlideItem> newSlides = activePeriods
                .SelectMany(p => p.Slides)
                .ToList();

            if (newSlides.Count == 0)
            {
                currentSlides.Clear();
                currentIndex = 0;
                slideTimer.Stop();
                SlideImage.Source = null;
                return;
            }

            bool slidesChanged =
                forceReload ||
                currentSlides.Count != newSlides.Count ||
                !currentSlides.Select(s => s.ImagePath).SequenceEqual(newSlides.Select(s => s.ImagePath)) ||
                !currentSlides.Select(s => s.DurationSeconds).SequenceEqual(newSlides.Select(s => s.DurationSeconds)) ||
                !currentSlides.Select(s => s.TransitionEffect).SequenceEqual(newSlides.Select(s => s.TransitionEffect));

            if (!slidesChanged)
                return;

            string? currentImagePath = null;

            if (currentSlides.Count > 0 && currentIndex >= 0 && currentIndex < currentSlides.Count)
                currentImagePath = currentSlides[currentIndex].ImagePath;

            currentSlides = newSlides;

            if (!string.IsNullOrEmpty(currentImagePath))
            {
                int foundIndex = currentSlides.FindIndex(s => s.ImagePath == currentImagePath);
                currentIndex = foundIndex >= 0 ? foundIndex : 0;
            }
            else
            {
                currentIndex = 0;
            }

            ShowCurrentSlide();
            RestartSlideTimer();
        }

        private void ShowCurrentSlide()
        {
            if (currentSlides.Count == 0)
            {
                SlideImage.Source = null;
                return;
            }

            SlideItem currentSlide = currentSlides[currentIndex];

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(currentSlide.ImagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                string effect = currentSlide.TransitionEffect ?? "Затухание";

                if (effect == "Затухание")
                {
                    FadeToImage(bitmap);
                }
                else
                {
                    SlideImage.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки изображения:\n{ex.Message}");
            }
        }

        private void FadeToImage(BitmapImage bitmap)
        {
            DoubleAnimation fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(250)
            };

            fadeOut.Completed += (s, e) =>
            {
                SlideImage.Source = bitmap;

                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(350)
                };

                SlideImage.BeginAnimation(OpacityProperty, fadeIn);
            };

            SlideImage.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void RestartSlideTimer()
        {
            if (currentSlides.Count == 0)
            {
                slideTimer.Stop();
                return;
            }

            int seconds = currentSlides[currentIndex].DurationSeconds;
            if (seconds <= 0)
                seconds = 5;

            slideTimer.Stop();
            slideTimer.Interval = TimeSpan.FromSeconds(seconds);
            slideTimer.Start();
        }
    }
}