using slider.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace slider.Services
{
    public class PlaylistRenderService
    {
        private Process? renderProcess;
        private bool stopRequested = false;
        public event Action? StreamingExited;
        public int? LastStreamingExitCode { get; private set; }
        public string BuildStreamArguments(
    List<SlideItem> slides,
    StreamSettings settings)
        {
            if (slides == null || slides.Count == 0)
                throw new InvalidOperationException("Нет слайдов для стрима.");

            var validSlides = slides
                .Where(s => File.Exists(s.Path))
                .ToList();

            if (validSlides.Count == 0)
                throw new InvalidOperationException("Нет валидных медиа файлов.");

            var inputArgs = new StringBuilder();
            var filterParts = new List<string>();

            double transitionDuration = 0.3;

            for (int i = 0; i < validSlides.Count; i++)
            {
                var slide = validSlides[i];

                int duration = slide.DurationSeconds > 0 ? slide.DurationSeconds : 5;

                string inputArg = "";

                if (slide.Type == MediaType.Image)
                {
                    inputArg = $"-re -loop 1 -t {duration} -i \"{slide.Path}\" ";
                }
                else
                {
                    string ssArg = slide.StartSeconds > 0
                        ? $"-ss {slide.StartSeconds.ToString(CultureInfo.InvariantCulture)} "
                        : "";

                    string tArg = "";

                    if (!slide.PlayFullVideo && slide.EndSeconds > slide.StartSeconds)
                    {
                        double cutDuration = slide.EndSeconds - slide.StartSeconds;
                        tArg = $"-t {cutDuration.ToString(CultureInfo.InvariantCulture)} ";
                    }

                    inputArg = $"{ssArg}-re -i \"{slide.Path}\" {tArg}";
                }

                inputArgs.Append(inputArg);

                double slideDuration = GetSlideDurationSeconds(slide);

                filterParts.Add(
                    $"[{i}:v]scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=decrease,pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2,fps={settings.Fps},format=yuv420p,trim=duration={slideDuration.ToString(CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS[v{i}]");

            }

            string currentLabel = "v0";
            double offset = Math.Max(0, GetSlideDurationSeconds(validSlides[0]) - transitionDuration);

            for (int i = 1; i < validSlides.Count; i++)
            {
                string nextLabel = $"v{i}";
                string outLabel = i == validSlides.Count - 1 ? "vout" : $"vx{i}";

                string transition = "fade";

                filterParts.Add(
                    $"[{currentLabel}][{nextLabel}]xfade=transition={transition}:duration={transitionDuration.ToString(CultureInfo.InvariantCulture)}:offset={offset.ToString(CultureInfo.InvariantCulture)}[{outLabel}]");

                currentLabel = outLabel;

                if (i < validSlides.Count - 1)
                {
                    offset += Math.Max(0, GetSlideDurationSeconds(validSlides[i]) - transitionDuration);
                }
            }

            string filterComplex = string.Join(";", filterParts);

            string args =
    $"{inputArgs}" +
    $"-filter_complex \"{filterComplex}\" " +
    $"-map \"[{currentLabel}]\" " +
    $"-an " +
    $"-c:v libx264 " +
    $"-preset ultrafast " +
    $"-tune zerolatency " +
    $"-pix_fmt yuv420p " +
    $"-aspect {settings.Width}:{settings.Height} " +
    $"-r {settings.Fps} " +
    $"-g {settings.Fps * 2} " +
    $"-keyint_min {settings.Fps} " +
    $"-sc_threshold 0 " +
    $"-fflags nobuffer " +
    $"-flags low_delay " +
    $"-b:v {settings.BitrateKbps}k " +
    $"-maxrate {settings.BitrateKbps}k " +
    $"-bufsize {settings.BitrateKbps * 2}k " +
    $"-muxdelay 0 " +
    $"-muxpreload 0 " +
    $"-f mpegts \"{settings.OutputUrl}\"";

            return args;
        }

        private double GetRealMediaDurationSeconds(string mediaPath, string ffmpegPath = "ffmpeg.exe")
        {
            if (string.IsNullOrWhiteSpace(mediaPath))
                return 0;

            if (!File.Exists(mediaPath))
                return 0;

            try
            {
                string ffprobePath;

                if (Path.GetFileName(ffmpegPath).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string? dir = Path.GetDirectoryName(ffmpegPath);

                    if (string.IsNullOrWhiteSpace(dir))
                        ffprobePath = "ffprobe.exe";
                    else
                        ffprobePath = Path.Combine(dir, "ffprobe.exe");
                }
                else
                {
                    ffprobePath = ffmpegPath.Replace("ffmpeg.exe", "ffprobe.exe", StringComparison.OrdinalIgnoreCase);
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffprobePath,
                        Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(3000);

                if (double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                    return duration;

                return 0;
            }
            catch
            {
                return 0;
            }
        }


        /// <summary>
        /// Deprecated legacy playlist-stream fallback. Production streaming uses
        /// <see cref="GdigrabStreamService"/>.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void StartStreaming(
            List<SlideItem> slides,
            StreamSettings settings,
            string ffmpegPath)
        {
            StopStreaming();

            stopRequested = false;
            LastStreamingExitCode = null;
            string args = BuildStreamArguments(slides, settings);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            process.Exited += (s, e) =>
            {
                if (!ReferenceEquals(renderProcess, process))
                    return;

                try
                {
                    LastStreamingExitCode = process.ExitCode;
                }
                catch
                {
                    LastStreamingExitCode = null;
                }

                if (!stopRequested)
                    StreamingExited?.Invoke();
            };

            renderProcess = process;
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        public void StopStreaming()
        {
            stopRequested = true;

            try
            {
                if (renderProcess != null)
                {
                    if (!renderProcess.HasExited)
                    {
                        try
                        {
                            renderProcess.Kill(true);
                        }
                        catch
                        {
                        }

                        try
                        {
                            renderProcess.WaitForExit(5000);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    renderProcess?.Dispose();
                }
                catch
                {
                }

                renderProcess = null;
            }
        }

        public bool IsStreaming()
        {
            return renderProcess != null && !renderProcess.HasExited;
        }

        private double GetSlideDurationSeconds(SlideItem slide)
        {
            if (slide.Type == MediaType.Image)
                return slide.DurationSeconds > 0 ? slide.DurationSeconds : 5;

            if (!slide.PlayFullVideo && slide.EndSeconds > slide.StartSeconds)
                return slide.EndSeconds - slide.StartSeconds;

            if (slide.PlayFullVideo)
            {
                double realDuration = GetRealMediaDurationSeconds(slide.Path, "ffmpeg.exe");
                if (realDuration > 0)
                    return realDuration;
            }

            if (slide.DurationSeconds > 0)
                return slide.DurationSeconds;

            return 10;
        }
    }
}
