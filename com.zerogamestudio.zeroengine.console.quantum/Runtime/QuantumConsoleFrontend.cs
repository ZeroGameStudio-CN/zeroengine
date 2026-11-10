using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using ZeroEngine.Console.Unity;
using ZeroEngine.Console.UI;

namespace ZeroEngine.Console.Quantum
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConsoleHost))]
    public sealed class QuantumConsoleFrontend : MonoBehaviour, IConsoleFrontend
    {
        [SerializeField] private GameObject quantumPrefab;
        [SerializeField] private QuantumCompatibilityProfile compatibility;
        [SerializeField] private UnityEngine.Object textFont;
        private ConsoleHost host;
        private GameObject owned;
        private Component console;
        private ScriptableObject ownedKeys;
        private IDisposable inputLease;
        private Type consoleType, responseType;
        private MethodInfo beginResponse, overrideInput, activate, deactivate, log, clear, invoke;
        private EventInfo stateChanged, deactivated;
        private bool needsArm, closing;
        private long flushedRevision;
        private long flushedClearRevision;
        private Action stateCallback, closeCallback;
        private Action<string> responseCallback;
        private Task<ConsoleResult> pending;
        private ConsoleInputOverlay overlay;
        private ConsoleEventSystemLease eventSystemLease;
        private CursorLockMode previousLock;
        private bool previousCursor;
        private GameObject previousSelection;
        public string BackendId => "quantum";
        public bool IsOpen { get; private set; }
        public bool IsComposing
        {
            get { try { return !string.IsNullOrEmpty(Input.compositionString); } catch (InvalidOperationException) { return false; } }
        }
        public Func<IDisposable> AcquireInputIsolation { get; set; }
        public GameObject OwnedInstance => owned;

        public void Configure(GameObject prefab, QuantumCompatibilityProfile profile)
        {
            Close(); quantumPrefab = prefab; compatibility = profile;
        }

        public void ConfigureFont(UnityEngine.Object font)
        {
            Close();
            textFont = font;
        }

        public bool TryInitialize(ConsoleHost owner, out string reason)
        {
            host = owner ? owner : GetComponent<ConsoleHost>();
            if (!host || host.Session == null) { reason = "Native Console session is unavailable."; return false; }
            if (!quantumPrefab || !compatibility) { reason = "Quantum prefab or verified compatibility profile is missing."; return false; }
            var source = quantumPrefab.GetComponentsInChildren<Component>(true).FirstOrDefault(component => component && component.GetType().FullName == "QFSW.QC.QuantumConsole");
            consoleType = source ? source.GetType() : null;
            if (!compatibility.Matches(consoleType, out reason)) return false;
            responseType = consoleType.Assembly.GetType("QFSW.QC.ResponseConfig", false);
            if (responseType == null) { reason = "Quantum response configuration is unavailable."; return false; }
            beginResponse = consoleType.GetMethod("BeginResponse", new[] { typeof(Action<string>), responseType });
            overrideInput = consoleType.GetMethod("OverrideConsoleInput", new[] { typeof(string), typeof(bool) });
            activate = consoleType.GetMethod("Activate", new[] { typeof(bool) });
            deactivate = consoleType.GetMethod("Deactivate", Type.EmptyTypes);
            log = consoleType.GetMethod("LogToConsoleAsync", new[] { typeof(string), typeof(LogType) });
            clear = consoleType.GetMethod("ClearConsole", Type.EmptyTypes);
            invoke = consoleType.GetMethod("InvokeCommand", Type.EmptyTypes);
            stateChanged = consoleType.GetEvent("OnStateChange");
            deactivated = consoleType.GetEvent("OnDeactivate");
            if (responseType == null || beginResponse == null || overrideInput == null || activate == null || deactivate == null || log == null || clear == null || invoke == null || stateChanged == null || deactivated == null)
            { reason = "Quantum public response API does not match the verified profile."; return false; }
            reason = string.Empty;
            return true;
        }

        public void Open()
        {
            if (IsOpen) return;
            if (!TryInitialize(host, out string reason)) throw new InvalidOperationException(reason);
            try
            {
                previousLock = Cursor.lockState; previousCursor = Cursor.visible;
                previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
                inputLease = AcquireInputIsolation?.Invoke();
                eventSystemLease = ConsoleEventSystemLease.Acquire(transform);
                owned = Instantiate(quantumPrefab, transform);
                console = owned.GetComponentsInChildren<Component>(true).First(component => component && component.GetType() == consoleType);
                DisableOwnedShortcutAndHistoryKeys();
                ConfigureOwnedButtons();
                consoleType.GetProperty("MaxStoredLogs")?.SetValue(console, 256);
                stateCallback = Rearm;
                closeCallback = Close;
                responseCallback = Receive;
                stateChanged.AddEventHandler(console, stateCallback);
                deactivated.AddEventHandler(console, closeCallback);
                IsOpen = true;
                activate.Invoke(console, new object[] { false });
                ApplyConfiguredFont();
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                CreateNativeInputOverlay();
                clear.Invoke(console, null);
                var snapshot = host.Session.GetOutputSnapshot();
                flushedRevision = snapshot.Revision;
                flushedClearRevision = snapshot.ClearRevision;
                foreach (var entry in snapshot.Entries) Send(entry);
                Arm();
            }
            catch { Close(); throw; }
        }

        private void Receive(string raw)
        {
            needsArm = true;
            overlay?.ResetAfterSubmit();
            host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Info, "> " + raw));
            pending = host.ExecuteAsync(raw);
        }

        private void Rearm()
        {
            if (!needsArm || !IsOpen || closing) return;
            needsArm = false;
            Arm();
            overrideInput.Invoke(console, new object[] { string.Empty, false });
        }

        private void Arm()
        {
            object config = Activator.CreateInstance(responseType);
            responseType.GetField("InputPrompt")?.SetValue(config, "ZE / ze-help");
            responseType.GetField("LogInput")?.SetValue(config, false);
            beginResponse.Invoke(console, new[] { responseCallback, config });
        }

        private void Update()
        {
            if (!IsOpen || !console || host.Session == null) return;
            if (!IsComposing && ConsoleKeyboard.Pressed(KeyCode.Escape)) { Close(); return; }
            if (pending != null && pending.IsCompleted)
            {
                try { pending.GetAwaiter().GetResult(); }
                catch (Exception exception) { host.Session.Write(new ConsoleOutput("", ConsoleOutputLevel.Error, exception.Message)); }
                pending = null;
            }
            long revision = host.Session.OutputRevision;
            if (revision == flushedRevision) return;
            var snapshot = host.Session.GetOutputSnapshot();
            var entries = snapshot.Entries;
            if (snapshot.ClearRevision != flushedClearRevision)
            {
                clear.Invoke(console, null);
                flushedClearRevision = snapshot.ClearRevision;
                flushedRevision = snapshot.ClearRevision;
            }
            int added = (int)Math.Min(entries.Count, snapshot.Revision - flushedRevision);
            for (int i = entries.Count - added; i < entries.Count; i++) Send(entries[i]);
            flushedRevision = snapshot.Revision;
        }

        private void Send(ConsoleOutput entry)
        {
            var level = entry.Level == ConsoleOutputLevel.Error ? LogType.Error : entry.Level == ConsoleOutputLevel.Warning ? LogType.Warning : LogType.Log;
            log.Invoke(console, new object[] { entry.Message, level });
        }

        private void CreateNativeInputOverlay()
        {
            var widget = owned.GetComponentsInChildren<Component>(true).FirstOrDefault(component => component && component.GetType().FullName == "TMPro.TMP_InputField");
            if (!widget) throw new InvalidOperationException("Verified Quantum input widget is missing.");
            var type = widget.GetType();
            var text = type.GetProperty("text");
            var caret = type.GetProperty("caretPosition");
            var focused = type.GetProperty("isFocused");
            if (text == null || caret == null || focused == null) throw new InvalidOperationException("Quantum public text-input API changed.");
            overlay = owned.AddComponent<ConsoleInputOverlay>();
            overlay.Initialize(host, new ConsoleInputAccess(
                () => (string)text.GetValue(widget),
                value => overrideInput.Invoke(console, new object[] { value, true }),
                () => (int)caret.GetValue(widget),
                () => (bool)focused.GetValue(widget),
                () => IsComposing), () => invoke.Invoke(console, null), Close);
        }

        private void ApplyConfiguredFont()
        {
            if (!textFont) return;
            bool applied = false;
            foreach (var component in owned.GetComponentsInChildren<Component>(true))
            {
                if (!component) continue;
                var property = component.GetType().GetProperty("font");
                if (property == null || property.PropertyType.FullName != "TMPro.TMP_FontAsset") continue;
                if (!property.PropertyType.IsInstanceOfType(textFont)) throw new ArgumentException("Quantum text font must be a TMP_FontAsset.");
                property.SetValue(component, textFont);
                applied = true;
            }
            if (!applied) throw new InvalidOperationException("Quantum text components are unavailable.");
        }

        private void ConfigureOwnedButtons()
        {
            foreach (var button in owned.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                Action action = null;
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                {
                    if (button.onClick.GetPersistentTarget(i) != console) continue;
                    switch (button.onClick.GetPersistentMethodName(i))
                    {
                        case "ClearConsole": action = () => host.Session?.ClearOutput(); break;
                        case "InvokeCommand": action = () => { if (!IsComposing) invoke.Invoke(console, null); }; break;
                        case "Deactivate": action = Close; break;
                    }
                }
                if (action == null) continue;
                var callback = action;
                button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                button.onClick.AddListener(() => callback());
            }
        }

        private void DisableOwnedShortcutAndHistoryKeys()
        {
            var property = consoleType.GetProperty("KeyConfig");
            var original = property?.GetValue(console) as ScriptableObject;
            if (!original) return;
            ownedKeys = Instantiate(original);
            var type = ownedKeys.GetType();
            foreach (string name in new[] { "ShowConsoleKey", "HideConsoleKey", "ToggleConsoleVisibilityKey", "SelectNextSuggestionKey", "SelectPreviousSuggestionKey" })
            {
                var field = type.GetField(name);
                if (field != null) field.SetValue(ownedKeys, Activator.CreateInstance(field.FieldType));
            }
            type.GetField("NextCommandKey")?.SetValue(ownedKeys, KeyCode.None);
            type.GetField("PreviousCommandKey")?.SetValue(ownedKeys, KeyCode.None);
            type.GetField("SubmitCommandKey")?.SetValue(ownedKeys, KeyCode.None);
            property.SetValue(console, ownedKeys);
        }

        public void Close()
        {
            if (closing) return;
            if (!IsOpen && !owned && inputLease == null && eventSystemLease == null) return;
            closing = true;
            try
            {
                if (console)
                {
                    if (stateCallback != null) stateChanged.RemoveEventHandler(console, stateCallback);
                    if (closeCallback != null) deactivated.RemoveEventHandler(console, closeCallback);
                    deactivate.Invoke(console, null);
                }
            }
            finally
            {
                IsOpen = false; needsArm = false;
                overlay = null;
                var events = EventSystem.current;
                if (events && (!events.currentSelectedGameObject || owned && events.currentSelectedGameObject.transform.IsChildOf(owned.transform)))
                    events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
                if (Cursor.lockState == CursorLockMode.None && Cursor.visible)
                { Cursor.lockState = previousLock; Cursor.visible = previousCursor; }
                if (owned) { owned.SetActive(false); Destroy(owned); }
                if (ownedKeys) Destroy(ownedKeys);
                owned = null; ownedKeys = null; console = null;
                stateCallback = null; closeCallback = null; responseCallback = null;
                try { inputLease?.Dispose(); }
                finally
                {
                    inputLease = null;
                    eventSystemLease?.Dispose(); eventSystemLease = null;
                    closing = false;
                }
            }
        }
        private void OnDisable() => Close();
        private void OnApplicationFocus(bool focused) { if (!focused) Close(); }
        private void OnDestroy() => Close();
    }
}
