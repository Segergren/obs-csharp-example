using System.Runtime.InteropServices;
using static LibObs.Obs;

namespace ObsTest
{
    class Program
    {
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int vsnprintf(IntPtr buffer, int count, string format, IntPtr args);
        private static log_handler_t _logHandler;

        static async Task Main(string[] args)
        {
            if (obs_initialized())
            {
                throw new Exception("error: obs already initialized");
            }

            _logHandler = CustomLogHandler;
            base_set_log_handler(_logHandler, IntPtr.Zero);

            Console.WriteLine("libobs version: " + obs_get_version_string());

            if (!obs_startup("en-US", null, IntPtr.Zero))
            {
                throw new Exception("error: obs_startup failed");
            }

            obs_add_data_path("./data/libobs/");
            obs_add_module_path(
                "./obs-plugins/64bit/",
                "./data/obs-plugins/%module%/"
            );

            // Now it's above obs_reset_video and it works! But laggy!
            // https://docs.obsproject.com/frontends#initialization-and-shutdown
            obs_load_all_modules();
            obs_log_loaded_modules();
            obs_post_load_modules();

            obs_video_info videoInfo = new obs_video_info()
            {
                adapter = 0,
                graphics_module = "libobs-d3d11",
                fps_num = 60,
                fps_den = 1,
                base_width = 2560,
                base_height = 1440,
                output_width = 2560,
                output_height = 1440,
                output_format = video_format.VIDEO_FORMAT_NV12,
                gpu_conversion = true,
                colorspace = video_colorspace.VIDEO_CS_DEFAULT,
                range = video_range_type.VIDEO_RANGE_DEFAULT,
                scale_type = obs_scale_type.OBS_SCALE_BILINEAR
            };

            int videoResetResult = obs_reset_video(ref videoInfo);
            if (videoResetResult != 0)
            {
                throw new Exception(
                    $"error: obs_reset_video failed with code {videoResetResult}"
                );
            }

            obs_audio_info audioInfo = new obs_audio_info()
            {
                samples_per_sec = 44100,
                speakers = speaker_layout.SPEAKERS_STEREO
            };

            if (!obs_reset_audio(ref audioInfo))
            {
                throw new Exception("error: obs_reset_audio failed");
            }

            // Create display capture source (video)
            IntPtr displayCaptureSettings = obs_data_create();
            obs_data_set_int(displayCaptureSettings, "monitor", 1);
            obs_data_set_int(displayCaptureSettings, "capture_method", 2);
            var displaySource = obs_source_create(
                "monitor_capture",
                "display",
                displayCaptureSettings,
                IntPtr.Zero
            );
            obs_data_release(displayCaptureSettings);
            if (displaySource == IntPtr.Zero)
            {
                throw new Exception("error: failed to create display source");
            }
            obs_set_output_source(0, displaySource); // Channel 0 for video

            // Create desktop audio capture source
            IntPtr audioCaptureSettings = obs_data_create();
            obs_data_set_string(audioCaptureSettings, "device_id", "default");
            var audioSource = obs_source_create(
                "wasapi_output_capture",
                "desktop_audio",
                audioCaptureSettings,
                IntPtr.Zero
            );
            obs_data_release(audioCaptureSettings);
            if (audioSource == IntPtr.Zero)
            {
                throw new Exception("error: failed to create audio source");
            }
            obs_set_output_source(1, audioSource); // Channel 1 for audio

            // Video encoder settings
            IntPtr videoEncoderSettings = obs_data_create();
            obs_data_set_string(videoEncoderSettings, "preset", "Quality");
            obs_data_set_string(videoEncoderSettings, "profile", "high");
            obs_data_set_bool(videoEncoderSettings, "use_bufsize", true);
            obs_data_set_string(videoEncoderSettings, "rate_control", "CBR");
            obs_data_set_int(videoEncoderSettings, "crf", 20);
            obs_data_set_int(videoEncoderSettings, "bitrate", 5000);

            // Create video encoder (try NVENC; fallback to x264 if needed)
            var videoEncoder = obs_video_encoder_create(
                "jim_nvenc",
                "simple_nvenc_recording",
                videoEncoderSettings,
                IntPtr.Zero
            );
            // Alternative: Software encoder fallback
            // var videoEncoder = obs_video_encoder_create("obs_x264", "simple_x264_recording", videoEncoderSettings, IntPtr.Zero);
            obs_data_release(videoEncoderSettings);
            if (videoEncoder == IntPtr.Zero)
            {
                throw new Exception(
                    "error: failed to create video encoder (check hardware/logs)"
                );
            }

            var videoPtr = obs_get_video();
            if (videoPtr == IntPtr.Zero)
            {
                throw new Exception("error: obs_get_video returned null");
            }
            obs_encoder_set_video(videoEncoder, videoPtr);

            // Audio encoder (simple AAC, no device_id needed here)
            IntPtr audioEncoderSettings = obs_data_create();
            obs_data_set_int(audioEncoderSettings, "bitrate", 160);
            var audioEncoder = obs_audio_encoder_create(
                "ffmpeg_aac",
                "simple_aac_recording",
                audioEncoderSettings,
                0,
                IntPtr.Zero
            );
            obs_data_release(audioEncoderSettings);
            if (audioEncoder == IntPtr.Zero)
            {
                throw new Exception("error: failed to create audio encoder");
            }
            obs_encoder_set_audio(audioEncoder, obs_get_audio());

            // Output setup
            IntPtr outputSettings = obs_data_create();
            obs_data_set_string(
                outputSettings,
                "path",
                "C:/Users/OlleS/source/repos/OBSTest/bin/Debug/net8.0/record.mp4"
            );
            var output = obs_output_create(
                "ffmpeg_muxer",
                "simple_ffmpeg_output",
                outputSettings,
                IntPtr.Zero
            );
            obs_data_release(outputSettings);
            if (output == IntPtr.Zero)
            {
                throw new Exception("error: failed to create output");
            }

            obs_output_set_video_encoder(output, videoEncoder);
            obs_output_set_audio_encoder(output, audioEncoder, 0);

            // Cancellation for stopping the recording loop
            var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            // Run recording in background thread with a processing loop
            var recordingTask = Task.Run(() =>
            {
                bool outputStarted = obs_output_start(output);
                if (!outputStarted)
                {
                    throw new Exception("error: failed to start output");
                }

                // Recording loop: Yield/tick until canceled
                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(1);
                }
            });

            Console.WriteLine("Recording started. Press any key to stop...");

            // Main thread: Non-blocking wait for key press
            while (!Console.KeyAvailable)
            {
                await Task.Delay(10); // Short non-blocking delay; reduce if needed
            }
            if (Console.KeyAvailable)
            {
                Console.ReadKey(true); // Consume the key press
            }

            // Signal stop and wait for cleanup
            cancellationTokenSource.Cancel();
            try
            {
                await recordingTask; // Wait for background task to finish
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Recording error: {ex.Message}");
            }

            obs_output_stop(output);
            obs_output_release(output);
            obs_encoder_release(audioEncoder);
            obs_encoder_release(videoEncoder);
            obs_source_release(displaySource);
            obs_source_release(audioSource);
        }

        private static void CustomLogHandler(int lvl, string msg, IntPtr args, IntPtr p)
        {
            const int bufferSize = 4096;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                vsnprintf(buffer, bufferSize, msg, args);
                string formattedMessage = Marshal.PtrToStringAnsi(buffer) ?? msg;

                string prefix = ((LogErrorLevel)lvl) switch
                {
                    LogErrorLevel.error => "[ERROR]",
                    LogErrorLevel.warning => "[WARNING]",
                    LogErrorLevel.info => "[INFO]",
                    LogErrorLevel.debug => "[DEBUG]",
                    _ => "[UNKNOWN]"
                };

                ConsoleColor color = ((LogErrorLevel)lvl) switch
                {
                    LogErrorLevel.error => ConsoleColor.Red,
                    LogErrorLevel.warning => ConsoleColor.Yellow,
                    LogErrorLevel.info => ConsoleColor.Green,
                    LogErrorLevel.debug => ConsoleColor.Gray,
                    _ => ConsoleColor.White
                };

                Console.ForegroundColor = color;
                Console.WriteLine($"{prefix} {formattedMessage}");
                Console.ResetColor();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}