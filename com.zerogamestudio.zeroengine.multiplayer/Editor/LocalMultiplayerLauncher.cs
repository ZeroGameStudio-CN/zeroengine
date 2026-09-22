using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Multiplayer.Local;
using Debug = UnityEngine.Debug;

namespace ZeroEngine.Multiplayer.Editor
{
    public sealed class LocalMultiplayerLauncher : EditorWindow
    {
        private string _playerExecutable = string.Empty;
        private string _address = "127.0.0.1";
        private int _port = 7770;
        private string _roomId = "local-room";
        private string _sessionId = "local-session";
        private string _productId = "sample";
        private string _protocolVersion = "1";
        private string _gameProtocolVersion = "1";
        private string _contentRevision = "sample";
        private string _buildVersion = "development";
        private string _gameRoomId = "sample-room";
        private bool _headless = true;
        private bool _exitOnReady;

        [MenuItem("Window/ZeroEngine/Multiplayer/Local Launcher")]
        private static void Open()
        {
            GetWindow<LocalMultiplayerLauncher>("Local Multiplayer");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Built Player", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _playerExecutable = EditorGUILayout.TextField("Executable", _playerExecutable);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                string selected = EditorUtility.OpenFilePanel("Select built player", string.Empty, "exe");
                if (!string.IsNullOrEmpty(selected))
                {
                    _playerExecutable = selected;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("LocalDirect", EditorStyles.boldLabel);
            _address = EditorGUILayout.TextField("Address", _address);
            _port = EditorGUILayout.IntField("Port", _port);
            _roomId = EditorGUILayout.TextField("Room ID", _roomId);
            _sessionId = EditorGUILayout.TextField("Session ID", _sessionId);
            _headless = EditorGUILayout.Toggle("Headless", _headless);
            _exitOnReady = EditorGUILayout.Toggle("Exit On Ready", _exitOnReady);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compatibility", EditorStyles.boldLabel);
            _productId = EditorGUILayout.TextField("Product", _productId);
            _protocolVersion = EditorGUILayout.TextField("Protocol", _protocolVersion);
            _gameProtocolVersion = EditorGUILayout.TextField("Game Protocol", _gameProtocolVersion);
            _contentRevision = EditorGUILayout.TextField("Content", _contentRevision);
            _buildVersion = EditorGUILayout.TextField("Build", _buildVersion);
            _gameRoomId = EditorGUILayout.TextField("Game Room", _gameRoomId);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!CanLaunch()))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Launch Host"))
                {
                    Launch(LocalMultiplayerRole.Host);
                }

                if (GUILayout.Button("Launch Client"))
                {
                    Launch(LocalMultiplayerRole.Client);
                }

                if (GUILayout.Button("Launch Both"))
                {
                    Launch(LocalMultiplayerRole.Host);
                    Launch(LocalMultiplayerRole.Client);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Open Logs Folder"))
            {
                Directory.CreateDirectory(GetLogsDirectory());
                EditorUtility.RevealInFinder(GetLogsDirectory());
            }
        }

        public static string BuildProcessArguments(
            LocalDevelopmentRoomOptions options,
            string logPath,
            bool headless)
        {
            string arguments = LocalMultiplayerLaunchArguments.Build(options) +
                               " -logFile " + Quote(logPath);
            if (headless)
            {
                arguments += " -batchmode -nographics";
            }

            return arguments;
        }

        private bool CanLaunch()
        {
            return File.Exists(_playerExecutable) && _port > 0 && _port <= ushort.MaxValue &&
                   !string.IsNullOrWhiteSpace(_roomId) && !string.IsNullOrWhiteSpace(_sessionId);
        }

        private void Launch(LocalMultiplayerRole role)
        {
            CompatibilityDescriptor compatibility = new CompatibilityDescriptor(
                _productId,
                _gameProtocolVersion,
                _contentRevision,
                _buildVersion,
                _gameRoomId);
            PlatformUser host = new PlatformUser(new PlatformUserId("local-host"), "Local Host");
            PlatformUser client = new PlatformUser(new PlatformUserId("local-client"), "Local Client");
            LocalDevelopmentRoomOptions options = new LocalDevelopmentRoomOptions(
                role,
                _address,
                (ushort)_port,
                new RoomId(_roomId),
                new SessionId(_sessionId),
                1,
                role == LocalMultiplayerRole.Host ? host : client,
                host,
                role == LocalMultiplayerRole.Host ? client : host,
                compatibility,
                _protocolVersion,
                2,
                RoomVisibility.Private,
                _exitOnReady);

            OperationResult validation = options.Validate();
            if (!validation.Succeeded)
            {
                Debug.LogError("[ZeroEngine.Multiplayer] Launcher options invalid: " + validation.MessageKey);
                return;
            }

            string logs = GetLogsDirectory();
            Directory.CreateDirectory(logs);
            string roleName = role == LocalMultiplayerRole.Host ? "host" : "client";
            string logPath = Path.Combine(logs, roleName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".log");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _playerExecutable,
                Arguments = BuildProcessArguments(options, logPath, _headless),
                WorkingDirectory = Path.GetDirectoryName(_playerExecutable) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = _headless
            };
            Process.Start(startInfo);
            Debug.Log("[ZeroEngine.Multiplayer] Launched " + roleName + ". Log: " + logPath);
        }

        private static string GetLogsDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LocalMultiplayer"));
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }
}
