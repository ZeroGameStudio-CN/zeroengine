using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ZeroGameStudio.UnityMcpControl.Editor
{
    [InitializeOnLoad]
    internal static class EditorControlMailbox
    {
        private const int SchemaVersion = 1;
        private const int MaxRequestBytes = 32 * 1024;
        private const double PollIntervalSeconds = 0.1;
        private const string CompanionVersion = "0.3.0";
        private const string UpstreamAssemblyName = "MCPForUnity.Editor";
        private const string ServiceLocatorTypeName = "MCPForUnity.Editor.Services.MCPServiceLocator";
        private const string ConfigurationCacheTypeName = "MCPForUnity.Editor.Services.EditorConfigurationCache";

        private const string UseHttpTransportKey = "MCPForUnity.UseHttpTransport";
        private const string HttpTransportScopeKey = "MCPForUnity.HttpTransportScope";
        private const string HttpBaseUrlKey = "MCPForUnity.HttpUrl";
        private const string AutoStartOnLoadKey = "MCPForUnity.AutoStartOnLoad";

        private static readonly string ProjectRoot = CanonicalPath(Path.Combine(Application.dataPath, ".."));
        private static readonly string ProjectHash = ComputeProjectHash(Application.dataPath);
        private static readonly int EditorPid = Process.GetCurrentProcess().Id;
        private static readonly long ProcessStartedAtMs = GetProcessStartedAtMs();
        private static readonly string SessionId = Guid.NewGuid().ToString("N");
        private static readonly string Token = Guid.NewGuid().ToString("N");
        private static readonly string StateRoot = GetStateRoot();
        private static readonly string DiscoveryPath = Path.Combine(StateRoot, "editor-control", "editors", ProjectHash + ".json");
        private static readonly string RequestDirectory = Path.Combine(StateRoot, "editor-control", "requests", ProjectHash);
        private static readonly string ResponseDirectory = Path.Combine(StateRoot, "editor-control", "responses", ProjectHash);
        private static readonly string UpstreamVersion = FindUpstreamVersion();

        private static bool _shuttingDown;
        private static bool _requestInFlight;
        private static double _nextPoll;

        static EditorControlMailbox()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }
            WriteDiscovery();
            EditorApplication.update += OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        private static void OnEditorUpdate()
        {
            if (_shuttingDown || _requestInFlight || EditorApplication.timeSinceStartup < _nextPoll)
            {
                return;
            }

            _nextPoll = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            if (!OwnsDiscovery())
            {
                WriteDiscovery();
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            string requestPath = FindNextRequest();
            if (!string.IsNullOrEmpty(requestPath))
            {
                _requestInFlight = true;
                ProcessRequestAsync(requestPath);
            }
        }

        private static string FindNextRequest()
        {
            try
            {
                if (!Directory.Exists(RequestDirectory))
                {
                    return null;
                }
                return Directory.GetFiles(RequestDirectory, "*.json")
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static async void ProcessRequestAsync(string requestPath)
        {
            ControlRequest request = null;
            try
            {
                request = ReadRequest(requestPath);
                if (request == null)
                {
                    return;
                }

                ControlResponse response = ValidateRequest(request);
                if (response == null)
                {
                    response = request.command == "status"
                        ? BuildStatusResponse(request)
                        : await ConnectAsync(request);
                }
                WriteResponse(response);
            }
            catch (Exception exception)
            {
                if (request != null)
                {
                    WriteResponse(NewResponse(request, false, "editor_control_failed", exception.GetType().Name));
                }
            }
            finally
            {
                TryDelete(requestPath);
                _requestInFlight = false;
            }
        }

        private static ControlRequest ReadRequest(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaxRequestBytes)
                {
                    return null;
                }
                return JsonUtility.FromJson<ControlRequest>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        private static ControlResponse ValidateRequest(ControlRequest request)
        {
            if (request.schema_version != SchemaVersion || !IsHexId(request.request_id))
            {
                return NewResponse(request, false, "invalid_request", "Unsupported control request.");
            }
            if (request.session_id != SessionId || request.token != Token)
            {
                return NewResponse(request, false, "unauthorized", "Control session mismatch.");
            }
            if (request.project_hash != ProjectHash || request.editor_pid != EditorPid ||
                !PathsEqual(request.project_root, ProjectRoot))
            {
                return NewResponse(request, false, "project_mismatch", "Control request targets a different Editor.");
            }
            if (request.expires_at_ms < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                return NewResponse(request, false, "request_expired", "Control request expired.");
            }
            if (request.command != "connect" && request.command != "status")
            {
                return NewResponse(request, false, "command_not_allowed", "Only connect and status are allowed.");
            }
            return null;
        }

        private static ControlResponse BuildStatusResponse(ControlRequest request)
        {
            object bridge;
            string error;
            if (!TryGetBridge(out bridge, out error))
            {
                return NewResponse(request, false, "upstream_api_unavailable", error);
            }
            bool running;
            if (!TryGetIsRunning(bridge, out running))
            {
                return NewResponse(request, false, "upstream_api_unavailable", "Bridge.IsRunning is unavailable.");
            }
            ControlResponse response = NewResponse(request, true, "ok", "Bridge status inspected.");
            response.is_running = running;
            return response;
        }

        private static async Task<ControlResponse> ConnectAsync(ControlRequest request)
        {
            if (!TryValidateEndpoint(request.endpoint))
            {
                return NewResponse(request, false, "invalid_endpoint", "Endpoint must be http://127.0.0.1:<port>.");
            }

            EditorPrefs.SetBool(UseHttpTransportKey, true);
            EditorPrefs.SetString(HttpTransportScopeKey, "local");
            EditorPrefs.SetString(HttpBaseUrlKey, request.endpoint);
            EditorPrefs.SetBool(AutoStartOnLoadKey, true);

            string error;
            if (!TryRefreshConfiguration(out error))
            {
                return NewResponse(request, false, "upstream_api_unavailable", error);
            }
            object bridge;
            if (!TryGetBridge(out bridge, out error))
            {
                return NewResponse(request, false, "upstream_api_unavailable", error);
            }

            bool started = await TryStartBridgeAsync(bridge);
            bool running;
            if (!started || !TryGetIsRunning(bridge, out running) || !running)
            {
                return NewResponse(request, false, "bridge_start_failed", "MCP for Unity Bridge did not connect.");
            }
            ControlResponse response = NewResponse(request, true, "ok", "MCP for Unity Bridge connected.");
            response.is_running = true;
            return response;
        }

        private static bool TryGetBridge(out object bridge, out string error)
        {
            bridge = null;
            Type locator = FindUpstreamType(ServiceLocatorTypeName);
            PropertyInfo property = locator == null ? null : locator.GetProperty("Bridge", BindingFlags.Public | BindingFlags.Static);
            if (property == null)
            {
                error = "Compatible MCPServiceLocator.Bridge API is unavailable.";
                return false;
            }
            try
            {
                bridge = property.GetValue(null);
                error = bridge == null ? "MCP Bridge service is unavailable." : null;
                return bridge != null;
            }
            catch
            {
                error = "MCP Bridge service could not be resolved.";
                return false;
            }
        }

        private static bool TryRefreshConfiguration(out string error)
        {
            Type type = FindUpstreamType(ConfigurationCacheTypeName);
            PropertyInfo instanceProperty = type == null ? null : type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            MethodInfo refreshMethod = type == null ? null : type.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Instance);
            if (instanceProperty == null || refreshMethod == null)
            {
                error = "Compatible EditorConfigurationCache.Refresh API is unavailable.";
                return false;
            }
            try
            {
                object instance = instanceProperty.GetValue(null);
                refreshMethod.Invoke(instance, null);
                error = null;
                return true;
            }
            catch
            {
                error = "MCP Editor configuration cache could not be refreshed.";
                return false;
            }
        }

        private static async Task<bool> TryStartBridgeAsync(object bridge)
        {
            try
            {
                MethodInfo method = bridge.GetType().GetMethod("StartAsync", BindingFlags.Public | BindingFlags.Instance);
                object value = method == null ? null : method.Invoke(bridge, null);
                Task task = value as Task;
                if (task == null)
                {
                    return false;
                }
                await task;
                PropertyInfo result = value.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                return result != null && result.GetValue(value) is bool && (bool)result.GetValue(value);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetIsRunning(object bridge, out bool running)
        {
            running = false;
            try
            {
                PropertyInfo property = bridge.GetType().GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !(property.GetValue(bridge) is bool))
                {
                    return false;
                }
                running = (bool)property.GetValue(bridge);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Type FindUpstreamType(string fullName)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(value => value.GetName().Name == UpstreamAssemblyName);
            return assembly == null ? null : assembly.GetType(fullName, false);
        }

        private static bool TryValidateEndpoint(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   uri.Scheme == Uri.UriSchemeHttp &&
                   uri.Host == "127.0.0.1" &&
                   uri.Port > 0 && uri.Port <= 65535 &&
                   string.IsNullOrEmpty(uri.UserInfo) &&
                   string.IsNullOrEmpty(uri.Query) &&
                   string.IsNullOrEmpty(uri.Fragment) &&
                   (uri.AbsolutePath == "/" || string.IsNullOrEmpty(uri.AbsolutePath));
        }

        private static ControlResponse NewResponse(ControlRequest request, bool ok, string code, string message)
        {
            return new ControlResponse
            {
                schema_version = SchemaVersion,
                request_id = request.request_id,
                session_id = SessionId,
                project_hash = ProjectHash,
                editor_pid = EditorPid,
                ok = ok,
                code = code,
                message = message,
                companion_version = CompanionVersion,
                upstream_version = UpstreamVersion,
            };
        }

        private static void WriteResponse(ControlResponse response)
        {
            if (response == null || !IsHexId(response.request_id))
            {
                return;
            }
            string path = Path.Combine(ResponseDirectory, response.request_id + ".json");
            AtomicWrite(path, JsonUtility.ToJson(response));
        }

        private static void WriteDiscovery()
        {
            var discovery = new DiscoveryRecord
            {
                schema_version = SchemaVersion,
                project_hash = ProjectHash,
                project_root = ProjectRoot,
                editor_pid = EditorPid,
                process_started_at_ms = ProcessStartedAtMs,
                session_id = SessionId,
                token = Token,
                companion_version = CompanionVersion,
                upstream_version = UpstreamVersion,
                created_at_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            AtomicWrite(DiscoveryPath, JsonUtility.ToJson(discovery));
        }

        private static bool OwnsDiscovery()
        {
            try
            {
                DiscoveryRecord current = File.Exists(DiscoveryPath)
                    ? JsonUtility.FromJson<DiscoveryRecord>(File.ReadAllText(DiscoveryPath, Encoding.UTF8))
                    : null;
                return current != null && current.session_id == SessionId && current.editor_pid == EditorPid;
            }
            catch
            {
                return false;
            }
        }

        private static void AtomicWrite(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, content + Environment.NewLine, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        private static void Shutdown()
        {
            if (_shuttingDown)
            {
                return;
            }
            _shuttingDown = true;
            EditorApplication.update -= OnEditorUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorApplication.quitting -= Shutdown;
            try
            {
                if (OwnsDiscovery())
                {
                    TryDelete(DiscoveryPath);
                }
            }
            catch
            {
                // A stale record is still rejected by PID, start time, path, and session checks.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort only; protocol validation rejects stale artifacts.
            }
        }

        private static string GetStateRoot()
        {
            string overridePath = Environment.GetEnvironmentVariable("UMCP_STATE_DIR");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }
#if UNITY_EDITOR_WIN
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UnityMcpSupervisor");
#elif UNITY_EDITOR_OSX
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support", "UnityMcpSupervisor");
#else
            string xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            string basePath = string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "state")
                : xdg;
            return Path.Combine(basePath, "unity-mcp-supervisor");
#endif
        }

        private static string FindUpstreamVersion()
        {
            try
            {
                UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(value => value.name == "com.coplaydev.unity-mcp");
                return package == null ? "unknown" : package.version;
            }
            catch
            {
                return "unknown";
            }
        }

        private static long GetProcessStartedAtMs()
        {
            try
            {
                return new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime())
                    .ToUnixTimeMilliseconds();
            }
            catch
            {
                return 0;
            }
        }

        private static string ComputeProjectHash(string dataPath)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(dataPath));
                var builder = new StringBuilder(16);
                for (int index = 0; index < 8; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string CanonicalPath(string value)
        {
            return Path.GetFullPath(value).Replace('\\', '/').TrimEnd('/');
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }
#if UNITY_EDITOR_WIN
            return string.Equals(CanonicalPath(left), CanonicalPath(right), StringComparison.OrdinalIgnoreCase);
#else
            return string.Equals(CanonicalPath(left), CanonicalPath(right), StringComparison.Ordinal);
#endif
        }

        private static bool IsHexId(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length == 32 &&
                   value.All(character => (character >= '0' && character <= '9') ||
                                          (character >= 'a' && character <= 'f'));
        }

        [Serializable]
        private sealed class DiscoveryRecord
        {
            public int schema_version;
            public string project_hash;
            public string project_root;
            public int editor_pid;
            public long process_started_at_ms;
            public string session_id;
            public string token;
            public string companion_version;
            public string upstream_version;
            public long created_at_ms;
        }

        [Serializable]
        private sealed class ControlRequest
        {
            public int schema_version;
            public string request_id;
            public string session_id;
            public string token;
            public string command;
            public string project_hash;
            public string project_root;
            public int editor_pid;
            public string endpoint;
            public long expires_at_ms;
        }

        [Serializable]
        private sealed class ControlResponse
        {
            public int schema_version;
            public string request_id;
            public string session_id;
            public string project_hash;
            public int editor_pid;
            public bool ok;
            public string code;
            public string message;
            public bool is_running;
            public string companion_version;
            public string upstream_version;
        }
    }
}
