using slider.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace slider.Services
{
    public class FfmpegOutputService
    {
        private Process? ffmpegProcess;

        public bool IsRunning => ffmpegProcess != null && !ffmpegProcess.HasExited;

        public void StartSlide(SlideItem slide, StreamSettings settings)
        {
            if (slide == null)
                throw new ArgumentNullException(nameof(slide));

            Stop();

            string inputPath = slide.Path;

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Файл медиа не найден.", inputPath);

            string args;

            if (slide.Type == MediaType.Image)
            {
                args =
                    $"-re -loop 1 -i \"{inputPath}\" " +
                    $"-vf scale={settings.Width}:{settings.Height},fps={settings.Fps} " +
                    $"-c:v {settings.VideoCodec} " +
                    $"-preset {settings.Preset} " +
                    $"-tune zerolatency " +
                    $"-pix_fmt yuv420p " +
                    $"-fflags nobuffer " +
                    $"-flags low_delay " +
                    $"-b:v {settings.BitrateKbps}k " +
                    $"-f {settings.Format} \"{settings.OutputUrl}\"";
            }
            else
            {
                string ssArg = slide.StartSeconds > 0
                    ? $"-ss {slide.StartSeconds.ToString(CultureInfo.InvariantCulture)} "
                    : "";

                string tArg = "";

                if (!slide.PlayFullVideo && slide.EndSeconds > slide.StartSeconds)
                {
                    double duration = slide.EndSeconds - slide.StartSeconds;
                    tArg = $"-t {duration.ToString(CultureInfo.InvariantCulture)} ";
                }

                args =
                    $"-re {ssArg}-i \"{inputPath}\" {tArg}" +
                    $"-vf scale={settings.Width}:{settings.Height},fps={settings.Fps} " +
                    $"-c:v {settings.VideoCodec} " +
                    $"-preset {settings.Preset} " +
                    $"-tune zerolatency " +
                    $"-pix_fmt yuv420p " +
                    $"-fflags nobuffer " +
                    $"-flags low_delay " +
                    $"-b:v {settings.BitrateKbps}k " +
                    $"-f {settings.Format} \"{settings.OutputUrl}\"";
            }

            ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = settings.FfmpegPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            ffmpegProcess.Start();
        }

        public double GetMediaDurationSeconds(string mediaPath, string ffmpegPath = "ffmpeg.exe")
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

        public void Stop()
        {
            try
            {
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                {
                    ffmpegProcess.Kill(true);
                    ffmpegProcess.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    ffmpegProcess?.Dispose();
                }
                catch
                {
                }

                ffmpegProcess = null;
            }
        }
    }
}