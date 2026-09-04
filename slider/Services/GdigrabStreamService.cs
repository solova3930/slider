using slider.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace slider.Services
{
    public static class GdigrabStreamService
    {
        private static readonly object SyncRoot = new();
        private static readonly Timer WatchdogTimer = new(WatchdogTick, null, Timeout.Infinite, Timeout.Infinite);
        private static Process? streamProcess;
        private static bool streamingRequested;
        private static IntPtr captureHwnd;
        private static StreamSettings? streamSettings;

        public static bool IsActive
        {
            get
            {
                lock (SyncRoot)
                {
                    return streamingRequested;
                }
            }
        }

        public static string Start(IntPtr hwnd, StreamSettings settings)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                throw new InvalidOperationException("Окно SliderWindow больше не существует.");

            lock (SyncRoot)
            {
                if (streamingRequested)
                    throw new InvalidOperationException("Production GDIGRAB-стрим уже запущен.");

                captureHwnd = hwnd;
                streamSettings = CloneSettings(settings);
                streamingRequested = true;

                try
                {
                    string command = StartProcessLocked();
                    WatchdogTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                    return command;
                }
                catch
                {
                    streamingRequested = false;
                    captureHwnd = IntPtr.Zero;
                    streamSettings = null;
                    throw;
                }
            }
        }

        public static void Stop()
        {
            Process? process;

            lock (SyncRoot)
            {
                streamingRequested = false;
                WatchdogTimer.Change(Timeout.Infinite, Timeout.Infinite);

                process = streamProcess;
                streamProcess = null;
                captureHwnd = IntPtr.Zero;
                streamSettings = null;
            }

            StopProcess(process);
        }

        private static void WatchdogTick(object? state)
        {
            Process? processToDispose = null;
            bool shouldStop = false;

            lock (SyncRoot)
            {
                if (!streamingRequested)
                    return;

                if (!IsWindow(captureHwnd))
                {
                    streamingRequested = false;
                    WatchdogTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    processToDispose = streamProcess;
                    streamProcess = null;
                    captureHwnd = IntPtr.Zero;
                    streamSettings = null;
                    shouldStop = true;
                }
                else if (streamProcess != null && !streamProcess.HasExited)
                {
                    return;
                }
                else
                {
                    processToDispose = streamProcess;
                    streamProcess = null;

                    try
                    {
                        StartProcessLocked();
                    }
                    catch
                    {
                        // Keep the requested state. The next watchdog tick will retry
                        // the same HWND with the same immutable settings snapshot.
                    }
                }
            }

            if (shouldStop)
                StopProcess(processToDispose);
            else
                processToDispose?.Dispose();
        }

        private static string StartProcessLocked()
        {
            if (streamSettings == null)
                throw new InvalidOperationException("Настройки production-стрима не заданы.");

            string arguments = BuildArguments(captureHwnd, streamSettings);
            string ffmpegPath = Path.IsPathRooted(streamSettings.FfmpegPath)
                ? streamSettings.FfmpegPath
                : Path.Combine(AppContext.BaseDirectory, streamSettings.FfmpegPath);
            string command = $"\"{ffmpegPath}\" {arguments}";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.ErrorDataReceived += (_, _) => { };
            process.OutputDataReceived += (_, _) => { };
            streamProcess = process;

            try
            {
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                return command;
            }
            catch
            {
                streamProcess = null;
                process.Dispose();
                throw;
            }
        }

        private static string BuildArguments(IntPtr hwnd, StreamSettings settings)
        {
            int fps = settings.Fps > 0 ? settings.Fps : 25;
            int bitrate = settings.BitrateKbps > 0 ? settings.BitrateKbps : 4000;
            string codec = string.IsNullOrWhiteSpace(settings.VideoCodec) ? "libx264" : settings.VideoCodec;
            string preset = string.IsNullOrWhiteSpace(settings.Preset) ? "veryfast" : settings.Preset;
            string format = string.IsNullOrWhiteSpace(settings.Format) ? "mpegts" : settings.Format;

            var arguments = new StringBuilder();
            arguments.Append($"-hide_banner -f gdigrab -framerate {fps} -draw_mouse 0 -i hwnd={hwnd.ToInt64()} ");
            arguments.Append("-an -vf \"crop=trunc(iw/2)*2:trunc(ih/2)*2\" ");
            arguments.Append($"-c:v {codec} -preset {preset} -tune zerolatency -pix_fmt yuv420p ");
            arguments.Append($"-r {fps} -g {fps * 2} -keyint_min {fps} -sc_threshold 0 ");
            arguments.Append($"-fflags nobuffer -flags low_delay -b:v {bitrate}k ");
            arguments.Append($"-maxrate {bitrate}k -bufsize {bitrate * 2}k -muxdelay 0 -muxpreload 0 ");
            arguments.Append($"-f {format} \"{settings.OutputUrl}\"");
            return arguments.ToString();
        }

        private static StreamSettings CloneSettings(StreamSettings settings)
        {
            return new StreamSettings
            {
                FfmpegPath = settings.FfmpegPath,
                OutputUrl = settings.OutputUrl,
                VideoCodec = settings.VideoCodec,
                Format = settings.Format,
                Fps = settings.Fps,
                BitrateKbps = settings.BitrateKbps,
                Preset = settings.Preset
            };
        }

        private static void StopProcess(Process? process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hwnd);
    }
}
