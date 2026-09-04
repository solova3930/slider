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
        private bool activeLayerIsA = true;
        private bool hasActiveLayer = false;
        private bool isTransitioning = false;

        private int mediaRequestId = 0;

        private const int CrossfadeMilliseconds = 350;
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
                .Where(p => p.IsActiveAt(now))
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

                ClearAllLayers();

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
                ClearAllLayers();
                return;
            }

            slideTimer.Stop();
            videoTimer?.Stop();

            SlideItem currentSlide = currentSlides[currentIndex];

            int requestId = ++mediaRequestId;

            try
            {
                string path = currentSlide.Path;

                if (!Path.IsPathRooted(path) &&
                    !string.IsNullOrWhiteSpace(playlistFilePath))
                {
                    string baseFolder =
                        Path.GetDirectoryName(playlistFilePath) ?? "";

                    path = Path.Combine(baseFolder, path);
                }

                bool targetLayerIsA =
                    !hasActiveLayer || !activeLayerIsA;

                Image targetImage = GetImage(targetLayerIsA);
                MediaElement targetVideo = GetVideo(targetLayerIsA);


                // =========================
                // КАРТИНКА
                // =========================

                if (currentSlide.Type == MediaType.Image)
                {
                    try
                    {
                        targetVideo.Stop();
                    }
                    catch
                    {
                    }

                    targetVideo.Source = null;
                    targetVideo.Visibility = Visibility.Collapsed;

                    BitmapImage bitmap = GetCachedImage(path);

                    targetImage.BeginAnimation(OpacityProperty, null);
                    targetImage.Source = bitmap;
                    targetImage.Opacity = 1;
                    targetImage.Visibility = Visibility.Visible;

                    ShowLayer(
                        targetLayerIsA,
                        currentSlide,
                        () =>
                        {
                            if (requestId != mediaRequestId)
                                return;

                            RestartSlideTimer();
                        });

                    return;
                }


                // =========================
                // ВИДЕО
                // =========================

                targetImage.BeginAnimation(OpacityProperty, null);
                targetImage.Source = null;
                targetImage.Visibility = Visibility.Collapsed;

                try
                {
                    targetVideo.Stop();
                }
                catch
                {
                }

                targetVideo.Source = null;
                targetVideo.Visibility = Visibility.Visible;


                RoutedEventHandler? openedHandler = null;

                openedHandler = (s, e) =>
                {
                    targetVideo.MediaOpened -= openedHandler;

                    // За время загрузки пользователь мог
                    // уже переключить слайд.
                    if (requestId != mediaRequestId)
                    {
                        try
                        {
                            targetVideo.Stop();
                        }
                        catch
                        {
                        }

                        return;
                    }

                    if (currentSlide.StartSeconds > 0)
                    {
                        targetVideo.Position =
                            TimeSpan.FromSeconds(currentSlide.StartSeconds);
                    }

                    targetVideo.Play();

                    ShowLayer(
                        targetLayerIsA,
                        currentSlide,
                        () =>
                        {
                            if (requestId != mediaRequestId)
                                return;

                            if (!currentSlide.PlayFullVideo)
                            {
                                StartVideoTimer(currentSlide);
                            }
                        });
                };

                targetVideo.MediaOpened += openedHandler;

                targetVideo.Source =
                    new Uri(path, UriKind.Absolute);
            }
            catch
            {
                ShowNextSlide();
            }
        }


        private Grid GetLayer(bool layerIsA)
        {
            return layerIsA ? LayerA : LayerB;
        }

        private Image GetImage(bool layerIsA)
        {
            return layerIsA ? SlideImageA : SlideImageB;
        }

        private MediaElement GetVideo(bool layerIsA)
        {
            return layerIsA ? SlideVideoA : SlideVideoB;
        }

        private MediaElement GetActiveVideo()
        {
            return GetVideo(activeLayerIsA);
        }


        private void ClearLayer(bool layerIsA)
        {
            Grid layer = GetLayer(layerIsA);
            Image image = GetImage(layerIsA);
            MediaElement video = GetVideo(layerIsA);

            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = 0;

            image.BeginAnimation(OpacityProperty, null);
            image.Source = null;
            image.Visibility = Visibility.Collapsed;

            try
            {
                video.Stop();
            }
            catch
            {
            }

            video.Source = null;
            video.Visibility = Visibility.Collapsed;
        }


        private void ClearAllLayers()
        {
            mediaRequestId++;

            slideTimer.Stop();
            videoTimer?.Stop();

            ClearLayer(true);
            ClearLayer(false);

            hasActiveLayer = false;
            isTransitioning = false;
        }


        private void ActivateLayerImmediately(bool targetLayerIsA)
        {
            bool oldLayerIsA = !targetLayerIsA;

            Grid targetLayer = GetLayer(targetLayerIsA);
            Grid oldLayer = GetLayer(oldLayerIsA);

            targetLayer.BeginAnimation(OpacityProperty, null);
            oldLayer.BeginAnimation(OpacityProperty, null);

            targetLayer.Opacity = 1;
            oldLayer.Opacity = 0;

            if (hasActiveLayer)
                ClearLayer(oldLayerIsA);

            activeLayerIsA = targetLayerIsA;
            hasActiveLayer = true;
            isTransitioning = false;
        }


        private void CrossfadeToLayer(
            bool targetLayerIsA,
            Action? completed = null)
        {
            Grid incomingLayer = GetLayer(targetLayerIsA);

            if (!hasActiveLayer)
            {
                incomingLayer.BeginAnimation(OpacityProperty, null);
                incomingLayer.Opacity = 1;

                activeLayerIsA = targetLayerIsA;
                hasActiveLayer = true;
                isTransitioning = false;

                completed?.Invoke();
                return;
            }

            bool outgoingLayerIsA = activeLayerIsA;

            if (outgoingLayerIsA == targetLayerIsA)
            {
                ActivateLayerImmediately(targetLayerIsA);
                completed?.Invoke();
                return;
            }

            Grid outgoingLayer = GetLayer(outgoingLayerIsA);

            incomingLayer.BeginAnimation(OpacityProperty, null);
            outgoingLayer.BeginAnimation(OpacityProperty, null);

            incomingLayer.Opacity = 0;
            outgoingLayer.Opacity = 1;

            isTransitioning = true;

            // Новый слой сразу считаем активным.
            // Это важно для MediaEnded и остальных событий.
            activeLayerIsA = targetLayerIsA;
            hasActiveLayer = true;

            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(CrossfadeMilliseconds)
            };

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(CrossfadeMilliseconds)
            };

            fadeIn.Completed += (s, e) =>
            {
                incomingLayer.BeginAnimation(OpacityProperty, null);
                outgoingLayer.BeginAnimation(OpacityProperty, null);

                incomingLayer.Opacity = 1;
                outgoingLayer.Opacity = 0;

                ClearLayer(outgoingLayerIsA);

                isTransitioning = false;

                completed?.Invoke();
            };

            incomingLayer.BeginAnimation(OpacityProperty, fadeIn);
            outgoingLayer.BeginAnimation(OpacityProperty, fadeOut);
        }


        private void ShowLayer(
            bool targetLayerIsA,
            SlideItem slide,
            Action? completed = null)
        {
            string effect = slide.TransitionEffect ?? "Затухание";

            if (effect == "Затухание" && hasActiveLayer)
            {
                CrossfadeToLayer(targetLayerIsA, completed);
            }
            else
            {
                ActivateLayerImmediately(targetLayerIsA);
                completed?.Invoke();
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

            double duration =
                slide.EndSeconds - slide.StartSeconds;

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

                ShowNextSlide();
            };

            videoTimer.Start();
        }



        private void SlideVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (currentSlides.Count == 0)
                return;

            if (sender is not MediaElement endedVideo)
                return;

            // Нас интересует только видео,
            // которое сейчас находится на активном слое.
            if (!ReferenceEquals(endedVideo, GetActiveVideo()))
                return;

            SlideItem currentSlide = currentSlides[currentIndex];

            if (currentSlide.Type == MediaType.Video &&
                currentSlide.PlayFullVideo)
            {
                ShowNextSlide();
            }
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

            if (isTransitioning)
                return;

            videoTimer?.Stop();

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

            if (isTransitioning)
                return;

            videoTimer?.Stop();

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