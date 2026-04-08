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


        public void StartStreaming(
            List<SlideItem> slides,
            StreamSettings settings,
            string ffmpegPath)
        {
            StopStreaming();

            stopRequested = false;
            string args = BuildStreamArguments(slides, settings);

            renderProcess = new Process
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

            renderProcess.Exited += (s, e) =>
            {
                if (!stopRequested)
                    StreamingExited?.Invoke();
            };

            renderProcess.Start();
            renderProcess.BeginErrorReadLine();
            renderProcess.BeginOutputReadLine();
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

        public string BuildRenderArguments(
            List<SlideItem> slides,
            StreamSettings settings,
            string outputPath)
        {
            if (slides == null || slides.Count == 0)
                throw new InvalidOperationException("Нет слайдов для рендера.");

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
                    inputArg = $"-loop 1 -t {duration.ToString(CultureInfo.InvariantCulture)} -i \"{slide.Path}\" ";
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

                    inputArg = $"{ssArg}-i \"{slide.Path}\" {tArg}";
                }

                inputArgs.Append(inputArg);

                filterParts.Add(
                    $"[{i}:v]scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=decrease,pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2,fps={settings.Fps},format=yuv420p,setpts=PTS-STARTPTS[v{i}]");
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
    $"-y " +
    $"{inputArgs}" +
    $"-filter_complex \"{filterComplex}\" " +
    $"-map \"[{currentLabel}]\" " +
    $"-an " +
    $"-c:v {settings.VideoCodec} " +
    $"-preset {settings.Preset} " +
    $"-pix_fmt yuv420p " +
    $"-aspect {settings.Width}:{settings.Height} " +
    $"-b:v {settings.BitrateKbps}k " +
    $"-movflags +faststart " +
    $"\"{outputPath}\"";

            return args;
        }

        public void RenderToFile(
            List<SlideItem> slides,
            StreamSettings settings,
            string ffmpegPath,
            string outputPath)
        {
            string args = BuildRenderArguments(slides, settings, outputPath);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput =false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception("FFmpeg render error:\n" + error);
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