// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// Mirrors every Unity log line to a fixed file in the app's external files
    /// directory, so the full session — <c>[WSDetection]</c> payload dumps,
    /// <c>[QVS:Renderer]</c> draw lines, the OpenXR startup report — survives the
    /// Android logcat ring buffer (which rolls over fast under this app's verbose
    /// per-frame logging, which is why <c>adb logcat -d</c> came back empty).
    ///
    /// The path is deterministic and known in advance, so the pull command can be
    /// scripted before the run:
    /// <code>
    ///   adb pull /sdcard/Android/data/com.zenithmoon.questvisionstream/files/qvs-latest.log
    /// </code>
    /// The previous session is rotated to <c>qvs-prev.log</c>. Self-contained: it
    /// installs itself before the first scene loads, independent of the bootstrap.
    /// Compile it out with the <c>QVS_NO_DEVICE_LOG</c> scripting define.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class DeviceLogRecorder : MonoBehaviour
    {
        /// <summary>Current session log — the deterministic adb-pull target.</summary>
        public const string LatestFileName = "qvs-latest.log";

        /// <summary>Prior session, rotated aside at startup.</summary>
        public const string PreviousFileName = "qvs-prev.log";

        private const float FlushIntervalSeconds = 0.5f;

        private static DeviceLogRecorder instance;

        private readonly object gate = new object();
        private StreamWriter writer;
        private bool writable;
        private bool dirty;
        private float nextFlushRealtime;

        /// <summary>Absolute on-device path of the current session log.</summary>
        public static string LatestLogPath => Path.Combine(Application.persistentDataPath, LatestFileName);

#if !QVS_NO_DEVICE_LOG
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null)
            {
                return;
            }

            var host = new GameObject("QVS_DeviceLogRecorder") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            instance = host.AddComponent<DeviceLogRecorder>();
        }
#endif

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            OpenFile();
        }

        private void OpenFile()
        {
            try
            {
                var directory = Application.persistentDataPath;
                var latest = Path.Combine(directory, LatestFileName);
                var previous = Path.Combine(directory, PreviousFileName);

                // Keep the last run alongside the current one; the current run is
                // always 'qvs-latest.log' so the pull command never changes.
                if (File.Exists(latest))
                {
                    if (File.Exists(previous))
                    {
                        File.Delete(previous);
                    }

                    File.Move(latest, previous);
                }

                writer = new StreamWriter(latest, false, new UTF8Encoding(false)) { AutoFlush = false };
                writable = true;

                writer.WriteLine("==== QuestVisionStream device log ====");
                writer.WriteLine($"opened   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"device   : {SystemInfo.deviceModel}");
                writer.WriteLine($"app      : {Application.identifier} v{Application.version} (unity {Application.unityVersion})");
                writer.WriteLine($"path     : {latest}");
                writer.WriteLine($"adb pull : adb pull /sdcard/Android/data/{Application.identifier}/files/{LatestFileName}");
                writer.WriteLine("======================================");
                writer.Flush();

                // Subscribe to the THREADED variant so logs raised off the main
                // thread (e.g. WebRTC/plugin callbacks) are captured too.
                Application.logMessageReceivedThreaded += OnLog;

                // Also emit through the normal log so the path lands in this file
                // and in logcat if it happens to survive.
                Debug.Log($"[QVS:Log] Recording session log to {latest}");
            }
            catch (Exception e)
            {
                writable = false;
                Debug.LogWarning($"[QVS:Log] Could not open device log file: {e.Message}");
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!writable)
            {
                return;
            }

            lock (gate)
            {
                if (writer == null)
                {
                    return;
                }

                try
                {
                    writer.Write(DateTime.Now.ToString("HH:mm:ss.fff"));
                    writer.Write(" [");
                    writer.Write(type.ToString());
                    writer.Write("] ");
                    writer.WriteLine(condition);

                    if ((type == LogType.Exception || type == LogType.Error) && !string.IsNullOrEmpty(stackTrace))
                    {
                        writer.WriteLine(stackTrace.TrimEnd());
                    }

                    dirty = true;
                }
                catch
                {
                    // Storage full or handle closed — stop trying rather than spam.
                    writable = false;
                }
            }
        }

        private void Update()
        {
            if (!dirty || Time.realtimeSinceStartup < nextFlushRealtime)
            {
                return;
            }

            nextFlushRealtime = Time.realtimeSinceStartup + FlushIntervalSeconds;
            Flush();
        }

        private void Flush()
        {
            lock (gate)
            {
                if (writer == null || !writable)
                {
                    return;
                }

                try
                {
                    writer.Flush();
                    dirty = false;
                }
                catch
                {
                    writable = false;
                }
            }
        }

        // Taking the headset off / backgrounding the app is the usual "end of test"
        // moment — persist immediately so a pull right after is complete.
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Flush();
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus)
            {
                Flush();
            }
        }

        private void OnApplicationQuit() => CloseFile();

        private void OnDestroy()
        {
            if (instance == this)
            {
                CloseFile();
            }
        }

        private void CloseFile()
        {
            Application.logMessageReceivedThreaded -= OnLog;

            lock (gate)
            {
                if (writer == null)
                {
                    return;
                }

                try
                {
                    writer.WriteLine($"==== closed {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
                    writer.Flush();
                    writer.Dispose();
                }
                catch
                {
                    // Best-effort close.
                }

                writer = null;
                writable = false;
            }
        }
    }
}
