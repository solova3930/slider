using slider.Models;
using slider.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private DispatcherTimer? videoTimer;

        private readonly string playlistFilePath = "";
        private DateTime lastFileWriteTime = DateTime.MinValue;
        private readonly Dictionary<string, BitmapImage> imageCache = new();
        public SliderWindow(List<PlaylistPeriod> playlistPeriods, string filePath)
        {
            InitializeComponent();

            periods = playlistPeriods ?? new List<PlaylistPeriod>();
            playlistFilePath = filePath ?? "";

            if (!string.IsNullOrWhiteSpace(playlistFilePath) && File.Exists(playlistFilePath))
            {
                lastFileWriteTime = File.GetLastWriteTime(playlistFilePath);
            }

            slideTimer = new DispatcherTimer();
            slideTimer.Tick += SlideTimer_Tick;

            periodCheckTimer = new DispatcherTimer();
            periodCheckTimer.Interval = TimeSpan.FromSeconds(10);
            periodCheckTimer.Tick += PeriodCheckTimer_Tick;
            periodCheckTimer.Start();

            ReloadActiveSlides(forceReload: true);
        }

        private BitmapImage GetCachedImage(string path)
        {
            if (imageCache.TryGetValue(path, out var cached))
                return cached;

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1920;
            bitmap.EndInit();
            bitmap.Freeze();

            imageCache[path] = bitmap;
            return bitmap;
        }

        private void PeriodCheckTimer_Tick(object? sender, EventArgs e)
        {
            CheckPlaylistFileChanges();
            ReloadActiveSlides(forceReload: false);
        }

        private void SliderWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
            Keyboard.Focus(this);
        }


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
                var data = PlaylistFileService.Load(playlistFilePath);

                periods.Clear();

                if (data.Periods != null)
                {
                    foreach (var p in data.Periods)
                        periods.Add(p);
                }
            }
            catch
            {
                // пока молча игнорируем, чтобы окно показа не падало
            }
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
                videoTimer?.Stop();
                SlideVideo.Stop();
                SlideVideo.Visibility = Visibility.Collapsed;
                SlideImage.Source = null;
                SlideImage.Visibility = Visibility.Collapsed;
                return;
            }

            bool slidesChanged =
                forceReload ||
                currentSlides.Count != newSlides.Count ||
                !currentSlides.Select(s => s.Path).SequenceEqual(newSlides.Select(s => s.Path)) ||
                !currentSlides.Select(s => s.Type).SequenceEqual(newSlides.Select(s => s.Type)) ||
                !currentSlides.Select(s => s.DurationSeconds).SequenceEqual(newSlides.Select(s => s.DurationSeconds)) ||
                !currentSlides.Select(s => s.TransitionEffect).SequenceEqual(newSlides.Select(s => s.TransitionEffect)) ||
                !currentSlides.Select(s => s.PlayFullVideo).SequenceEqual(newSlides.Select(s => s.PlayFullVideo)) ||
                !currentSlides.Select(s => s.StartSeconds).SequenceEqual(newSlides.Select(s => s.StartSeconds)) ||
                !currentSlides.Select(s => s.EndSeconds).SequenceEqual(newSlides.Select(s => s.EndSeconds));

            if (!slidesChanged)
                return;

            string? currentPath = null;

            if (currentSlides.Count > 0 &&
                currentIndex >= 0 &&
                currentIndex < currentSlides.Count)
            {
                currentPath = currentSlides[currentIndex].Path;
            }

            currentSlides = newSlides;

            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                int foundIndex = currentSlides.FindIndex(s => s.Path == currentPath);
                currentIndex = foundIndex >= 0 ? foundIndex : 0;
            }
            else
            {
                currentIndex = 0;
            }

            ShowCurrentSlide();
        }



        private void ShowCurrentSlide()
        {
            if (currentSlides.Count == 0)
            {
                slideTimer.Stop();
                videoTimer?.Stop();
                SlideVideo.Stop();
                SlideVideo.Visibility = Visibility.Collapsed;
                SlideImage.Source = null;
                SlideImage.Visibility = Visibility.Collapsed;
                return;
            }

            SlideItem currentSlide = currentSlides[currentIndex];

            try
            {
                string path = currentSlide.Path;

                if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(playlistFilePath))
                {
                    string baseFolder = Path.GetDirectoryName(playlistFilePath) ?? "";
                    path = Path.Combine(baseFolder, path);
                }

                if (currentSlide.Type == MediaType.Image)
                {
                    videoTimer?.Stop();
                    SlideVideo.Stop();
                    SlideVideo.Source = null;
                    SlideVideo.Visibility = Visibility.Collapsed;

                    BitmapImage bitmap = GetCachedImage(path);

                    string effect = currentSlide.TransitionEffect ?? "Затухание";

                    SlideImage.Visibility = Visibility.Visible;

                    if (effect == "Затухание")
                    {
                        FadeToImage(bitmap);
                    }
                    else
                    {
                        SlideImage.BeginAnimation(OpacityProperty, null);
                        SlideImage.Source = bitmap;
                        SlideImage.Opacity = 1;
                    }

                    RestartSlideTimer();
                }
                else if (currentSlide.Type == MediaType.Video)
                {
                    slideTimer.Stop();
                    videoTimer?.Stop();

                    SlideImage.Source = null;
                    SlideImage.Visibility = Visibility.Collapsed;

                    SlideVideo.Stop();
                    SlideVideo.Source = null;
                    SlideVideo.Visibility = Visibility.Visible;
                    SlideVideo.Source = new Uri(path, UriKind.Absolute);

                    RoutedEventHandler? openedHandler = null;
                    openedHandler = (s, e) =>
                    {
                        SlideVideo.MediaOpened -= openedHandler;

                        if (currentSlide.StartSeconds > 0)
                            SlideVideo.Position = TimeSpan.FromSeconds(currentSlide.StartSeconds);

                        SlideVideo.Play();

                        if (!currentSlide.PlayFullVideo)
                            StartVideoTimer(currentSlide);
                    };

                    SlideVideo.MediaOpened += openedHandler;
                }
            }
            catch
            {
                ShowNextSlide();
            }
        }

        private void EffectComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                comboBox.IsDropDownOpen = true;
                comboBox.Focus();
            }
        }

        private void StartVideoTimer(SlideItem slide)
        {
            videoTimer?.Stop();

            double duration = slide.EndSeconds - slide.StartSeconds;

            if (duration <= 0)
                duration = slide.DurationSeconds;

            if (duration <= 0)
                duration = 1;

            videoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(duration)
            };

            videoTimer.Tick += (s, e) =>
            {
                videoTimer?.Stop();
                SlideVideo.Stop();
                ShowNextSlide();
            };

            videoTimer.Start();
        }


        private void SlideVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (currentSlides.Count == 0)
                return;

            SlideItem currentSlide = currentSlides[currentIndex];

            if (currentSlide.Type == MediaType.Video && currentSlide.PlayFullVideo)
            {
                ShowNextSlide();
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

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private void ShowPreviousSlide()
        {
            if (currentSlides.Count == 0)
                return;

            videoTimer?.Stop();
            SlideVideo.Stop();

            currentIndex--;

            if (currentIndex < 0)
                currentIndex = currentSlides.Count - 1;

            ShowCurrentSlide();
        }

        private void SlideTimer_Tick(object? sender, EventArgs e)
        {
            if (currentSlides.Count == 0)
            {
                slideTimer.Stop();
                return;
            }

            ShowNextSlide();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                if (slideTimer.IsEnabled)
                    slideTimer.Stop();
                else
                    slideTimer.Start();

                e.Handled = true;
                return;
            }

            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
                return;
            }

            if (currentSlides.Count == 0)
                return;

            if (e.Key == Key.Right)
            {
                ShowNextSlide();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                ShowPreviousSlide();
                e.Handled = true;
            }
        }

        private void ToggleFullscreen()
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleFullscreen();
        }



        private void ShowNextSlide()
        {
            if (currentSlides.Count == 0)
                return;

            videoTimer?.Stop();
            SlideVideo.Stop();

            currentIndex++;

            if (currentIndex >= currentSlides.Count)
                currentIndex = 0;

            ShowCurrentSlide();
        }

        private void RestartSlideTimer()
        {
            if (currentSlides.Count == 0)
            {
                slideTimer.Stop();
                return;
            }

            if (currentSlides[currentIndex].Type == MediaType.Video)
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