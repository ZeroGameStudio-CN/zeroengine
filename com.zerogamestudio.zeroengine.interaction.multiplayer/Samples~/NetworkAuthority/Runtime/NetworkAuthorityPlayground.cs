using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.Interaction.Multiplayer.Samples
{
    public enum AuthorityPlaygroundMode { Offline, Host, Client }

    /// <summary>Optional sample scene only. The client constructs a view and input session, never the authority world.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkAuthorityPlayground : MonoBehaviour
    {
        [SerializeField] private AuthorityPlaygroundMode mode;
        [SerializeField] private int port = 7779;
        [SerializeField] private bool startAutomatically = true;
        private AuthorityDemoHost host;
        private AuthorityDemoClient client;
        private readonly List<AuthorityDemoHost> retiringHosts = new List<AuthorityDemoHost>();
        private readonly List<AuthorityDemoClient> retiringClients = new List<AuthorityDemoClient>();
        private UnityEngine.UI.Text status, outcome;
        private UnityEngine.UI.InputField portInput;
        private Font font;
        private string selected = "door", failure;
        private static int backgroundUsers;
        private static bool backgroundBefore;
        private bool ownsBackground;
        private double previousTime;
        public UnityEngine.UI.Button InteractButton { get; private set; }
        public bool IsAuthority => host != null;
        public int ListeningPort => host?.Port ?? port;
        public bool IsConnected => host != null || client?.Connected == true;
        public DemoClientState State => host?.Local ?? client?.State;
        public bool WorkersEnded => (host?.WorkersEnded ?? true) && (client?.WorkersEnded ?? true)
            && retiringHosts.TrueForAll(p => p.WorkersEnded) && retiringClients.TrueForAll(p => p.WorkersEnded);

        private void Awake() { BuildUi(); }
        private void OnEnable()
        {
            if (backgroundUsers++ == 0) backgroundBefore = Application.runInBackground;
            Application.runInBackground = true; ownsBackground = true;
            previousTime = Time.realtimeSinceStartupAsDouble;
            if (startAutomatically) StartMode(mode, port);
        }
        private void OnDisable()
        {
            Stop();
            if (ownsBackground && --backgroundUsers == 0 && Application.runInBackground) Application.runInBackground = backgroundBefore;
            ownsBackground = false;
        }
        public void Configure(AuthorityPlaygroundMode selectedMode, int selectedPort, bool autoStart = true)
        {
            mode = selectedMode; port = selectedPort; startAutomatically = autoStart;
            if (isActiveAndEnabled && autoStart) StartMode(selectedMode, selectedPort);
        }
        public void StartMode(AuthorityPlaygroundMode selectedMode, int selectedPort)
        {
            Stop(); failure = null;
            try
            {
                mode = selectedMode; port = selectedPort;
                if (mode == AuthorityPlaygroundMode.Client) client = new AuthorityDemoClient(port);
                else
                {
                    host = new AuthorityDemoHost(1, mode == AuthorityPlaygroundMode.Host, port);
                    if (mode == AuthorityPlaygroundMode.Host) { port = host.Port; portInput.text = port.ToString(); }
                }
            }
            catch (Exception error) { failure = error.Message; Stop(); }
        }
        public void Stop()
        {
            Cancel();
            if (host != null) { host.Dispose(); retiringHosts.Add(host); host = null; }
            if (client != null) { client.Dispose(); retiringClients.Add(client); client = null; }
        }
        public void Approach(string target)
        {
            Cancel(); selected = target; Send(new DemoPacket { Kind = "approach", Name = target });
        }
        public void LeaveRange() { Cancel(); Send(new DemoPacket { Kind = "leave" }); }
        public void Press() { Send(State?.Press(selected)); }
        public void Release() { Send(State?.Edge("release")); }
        public void Cancel() { Send(State?.Edge("cancel")); }
        public void Confirm() { Send(State?.Edge("confirm")); }
        private void Send(DemoPacket packet)
        {
            if (packet == null) return;
            if (host != null) host.SubmitLocal(packet); else client?.Send(packet);
        }
        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble, delta = now - previousTime; previousTime = now;
            host?.Pump(delta); client?.Pump();
            retiringHosts.RemoveAll(p => p.WorkersEnded); retiringClients.RemoveAll(p => p.WorkersEnded);
            var view = State?.View;
            if (view == null) { status.text = failure ?? client?.Failure ?? "Waiting for authority connection…"; return; }
            var self = State.Self;
            status.text = mode + " | " + (self?.Name ?? "unbound") + " | Selected: " + selected +
                "\nCoins: " + (self?.Coins ?? 0) + "   Reserved: " + (self?.Reserved ?? 0) +
                "   Gifts: " + (self?.Gifts ?? 0) + "   Conversations: " + (self?.Conversations ?? 0) +
                "   Purchases: " + (self?.Purchases ?? 0) + "\nDoor: " + (view.DoorOpen ? "open" : "closed") +
                "   Shared pickup: " + view.SharedPickups + " / winner: " + (view.SharedWinner ?? "none") +
                "   Personal pickup: " + view.PersonalPickups;
            var result = State.Outcomes?.LatestOutcome;
            outcome.text = failure ?? client?.Failure ?? (result.HasValue
                ? result.Value.Gesture + " → " + result.Value.Status + (string.IsNullOrEmpty(result.Value.Reason) ? "" : " — " + result.Value.Reason)
                : "Approach a target, then press or hold Interact. Shop confirmation settles on the authority.");
        }
        private void BuildUi()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = new GameObject("Authority Playground UI", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.transform.SetParent(transform, false); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1000, 760); scaler.matchWidthOrHeight = .5f;
            var panel = new GameObject("Controls", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.transform.SetParent(canvas.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(22, 22); rect.offsetMax = new Vector2(-22, -22);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(.055f, .075f, .11f, .99f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 9; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            Label(panel.transform, "ZE Interaction Authority Playground", 38);
            Label(panel.transform, "Offline or independent local host/client • authority owns every gameplay change", 30);
            status = Label(panel.transform, "", 94); outcome = Label(panel.transform, "", 52);
            var modes = Row(panel.transform, "Mode");
            portInput = Field(modes, port.ToString());
            MakeButton(modes, "Offline", () => StartMode(AuthorityPlaygroundMode.Offline, 0));
            MakeButton(modes, "Host", () => StartWithPort(AuthorityPlaygroundMode.Host));
            MakeButton(modes, "Join", () => StartWithPort(AuthorityPlaygroundMode.Client));
            MakeButton(modes, "Stop", Stop);
            MakeButton(panel.transform, "Approach door — tap to toggle", () => Approach("door"));
            MakeButton(panel.transform, "Approach NPC — tap to talk / hold to gift", () => Approach("npc"));
            MakeButton(panel.transform, "Approach shared pickup — one winner", () => Approach("shared"));
            MakeButton(panel.transform, "Approach personal pickup — host owner only", () => Approach("personal"));
            MakeButton(panel.transform, "Approach shop — reserve two coins, then confirm or cancel", () => Approach("shop"));
            InteractButton = MakeButton(panel.transform, "Interact — press / hold / release", null);
            var hold = InteractButton.gameObject.AddComponent<AuthoritySampleHoldButton>(); hold.Owner = this;
            var actions = Row(panel.transform, "Actions"); MakeButton(actions, "Confirm shop", Confirm);
            MakeButton(actions, "Cancel", Cancel); MakeButton(actions, "Leave range", LeaveRange);
            if (!EventSystem.current)
            {
                var events = new GameObject("Authority Playground EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
        }
        private void StartWithPort(AuthorityPlaygroundMode selectedMode)
        {
            if (!int.TryParse(portInput.text, out int value) || value < 0 || value > 65535 || selectedMode == AuthorityPlaygroundMode.Client && value == 0)
            { failure = "Choose a valid loopback port."; return; }
            StartMode(selectedMode, value);
        }
        private UnityEngine.UI.Text Label(Transform parent, string text, float height)
        {
            var obj = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text), typeof(UnityEngine.UI.LayoutElement)); obj.transform.SetParent(parent, false);
            var label = obj.GetComponent<UnityEngine.UI.Text>(); label.font = font; label.fontSize = 19; label.color = new Color(.9f, .94f, 1);
            label.text = text; label.alignment = TextAnchor.MiddleLeft; obj.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height; return label;
        }
        private Transform Row(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement)); obj.transform.SetParent(parent, false);
            obj.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().spacing = 9; obj.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 42; return obj.transform;
        }
        private UnityEngine.UI.Button MakeButton(Transform parent, string title, UnityEngine.Events.UnityAction action)
        {
            var obj = new GameObject(title, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement)); obj.transform.SetParent(parent, false);
            obj.GetComponent<UnityEngine.UI.Image>().color = new Color(.13f, .22f, .32f); obj.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 42;
            var label = Label(obj.transform, title, 42); label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            var rect = label.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var button = obj.GetComponent<UnityEngine.UI.Button>(); if (action != null) button.onClick.AddListener(action); return button;
        }
        private UnityEngine.UI.InputField Field(Transform parent, string value)
        {
            var obj = new GameObject("Port", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.InputField), typeof(UnityEngine.UI.LayoutElement)); obj.transform.SetParent(parent, false);
            obj.GetComponent<UnityEngine.UI.Image>().color = new Color(.1f, .13f, .18f);
            var text = Label(obj.transform, value, 42); text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(8, 0); rect.offsetMax = Vector2.zero;
            var input = obj.GetComponent<UnityEngine.UI.InputField>(); input.textComponent = text; input.text = value; input.contentType = UnityEngine.UI.InputField.ContentType.IntegerNumber;
            return input;
        }
    }

    public sealed class AuthoritySampleHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public NetworkAuthorityPlayground Owner;
        private global::ZeroEngine.Interaction.Core.Authority.InteractionInputSession pressed;
        public void OnPointerDown(PointerEventData data) { pressed = Owner.State?.Input; Owner.Press(); }
        public void OnPointerUp(PointerEventData data)
        { if (ReferenceEquals(pressed, Owner.State?.Input)) Owner.Release(); pressed = null; }
        public void OnPointerExit(PointerEventData data)
        { if (pressed != null && ReferenceEquals(pressed, Owner.State?.Input)) Owner.Cancel(); pressed = null; }
        private void OnDisable()
        { if (pressed != null && Owner && ReferenceEquals(pressed, Owner.State?.Input)) Owner.Cancel(); pressed = null; }
    }
}
