using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Unity;

namespace ZeroEngine.Interaction.Samples
{
    /// <summary>A self-contained consumer with two wallets, four target types and delayed shop confirmation.</summary>
    public sealed class InteractionPlayground : MonoBehaviour
    {
        private readonly Dictionary<string, InteractionTargetBehaviour> targets = new Dictionary<string, InteractionTargetBehaviour>();
        private readonly Dictionary<string, int> wallets = new Dictionary<string, int>();
        private readonly Dictionary<string, InteractionRequest> confirmations = new Dictionary<string, InteractionRequest>();
        private readonly List<InteractionAgentBehaviour> actors = new List<InteractionAgentBehaviour>();
        private UnityEngine.UI.Text status, outcome;
        private UnityEngine.UI.Button confirm;
        private Font font;
        private string lastOutcome = "Choose a target, then press or hold Interact.";
        public InteractionAgentBehaviour SelectedActor { get; private set; }
        public bool DoorOpen { get; private set; }
        public int Conversations { get; private set; }
        public int Gifts { get; private set; }
        public int Pickups { get; private set; }
        public int Purchases { get; private set; }
        public SampleHoldButton InteractButton { get; private set; }
        public int Coins => wallets[SelectedActor.ActorId];
        public InteractionRequest PendingConfirmation => confirmations.TryGetValue(SelectedActor.ActorId, out var value) ? value : null;

        private void Awake()
        {
            var scopeObject = Child("Interaction world", transform);
            var world = scopeObject.AddComponent<InteractionWorldBehaviour>();
            CreateActor(world, "actor-a", 5); CreateActor(world, "actor-b", 3);
            SelectedActor = actors[0];
            var door = CreateTarget(world, "door", 0, InteractionPolicy.Press);
            door.Requested += request => request.TryCommit(() => DoorOpen = !DoorOpen);
            var npc = CreateTarget(world, "npc", 5, new InteractionPolicy(InteractionMode.TapOrHold, .7, .4));
            npc.Checking += context => context.Gesture == InteractionGesture.Tap || wallets[context.ActorId] > 0
                ? InteractionCheck.Permit : InteractionCheck.Deny("A gift costs one coin.");
            npc.Requested += request => request.TryCommit(() =>
            {
                if (request.Context.Gesture == InteractionGesture.Tap) Conversations++;
                else { wallets[request.Context.ActorId]--; Gifts++; }
            });
            var pickup = CreateTarget(world, "pickup", 10, InteractionPolicy.Press);
            pickup.Requested += request => request.TryCommit(() => { Pickups++; pickup.SetAvailable(false); });
            var shop = CreateTarget(world, "shop", 15, InteractionPolicy.Press);
            shop.Checking += context => wallets[context.ActorId] >= 2 ? InteractionCheck.Permit : InteractionCheck.Deny("Purchase costs two coins.");
            shop.Requested += request =>
            {
                confirmations[request.Context.ActorId] = request;
                request.Cancelled += reason => confirmations.Remove(request.Context.ActorId);
            };
            BuildUi();
        }

        private void CreateActor(InteractionWorldBehaviour world, string id, int coins)
        {
            var node = Child(id, world.transform); node.transform.position = Vector3.left * 20;
            var actor = node.AddComponent<InteractionAgentBehaviour>(); actor.Configure(world, id, 1.5f);
            wallets.Add(id, coins); actors.Add(actor);
            actor.Runtime.Completed += result => lastOutcome = result.Context.ActorId + " / " + result.Context.TargetId +
                ": " + result.Status + (string.IsNullOrEmpty(result.Reason) ? "" : " — " + result.Reason);
        }

        private InteractionTargetBehaviour CreateTarget(InteractionWorldBehaviour world, string id, float x, InteractionPolicy policy)
        {
            var node = Child(id, world.transform); node.transform.position = Vector3.right * x;
            var target = node.AddComponent<InteractionTargetBehaviour>(); target.Configure(world, id, policy);
            targets.Add(id, target); return target;
        }

        public void SelectActor(int index)
        {
            if (index < 0 || index >= actors.Count) throw new ArgumentOutOfRangeException(nameof(index));
            SelectedActor.Cancel(); SelectedActor = actors[index];
        }
        public void Approach(string id)
        {
            SelectedActor.transform.position = targets[id].transform.position + Vector3.down * .25f;
            SelectedActor.Runtime.RefreshFocus();
        }
        public void LeaveRange()
        {
            SelectedActor.transform.position = Vector3.left * 20;
            SelectedActor.Runtime.RefreshFocus();
        }
        public bool ConfirmPurchase()
        {
            var request = PendingConfirmation;
            if (request == null) return false;
            bool committed = request.TryCommit(() => { wallets[request.Context.ActorId] -= 2; Purchases++; });
            confirmations.Remove(request.Context.ActorId);
            return committed;
        }
        public void CancelPurchase() { PendingConfirmation?.TryCancel(); }
        public void RespawnPickup()
        {
            var pickup = targets["pickup"];
            pickup.gameObject.SetActive(false); pickup.SetAvailable(true); pickup.gameObject.SetActive(true);
            SelectedActor.Runtime.RefreshFocus();
        }

        private void Update()
        {
            if (!status || SelectedActor.Runtime == null) return;
            status.text = SelectedActor.ActorId + " | Coins: " + Coins + " | Focus: " + (SelectedActor.Runtime.Focus?.Id ?? "none") +
                " | Hold: " + SelectedActor.Runtime.Progress.ToString("P0") + "\nDoor: " + (DoorOpen ? "open" : "closed") +
                "   Talk: " + Conversations + "   Gifts: " + Gifts + "   Pickups: " + Pickups + "   Purchases: " + Purchases;
            outcome.text = lastOutcome;
            confirm.interactable = PendingConfirmation != null && PendingConfirmation.IsCurrent;
        }

        private void BuildUi()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasObject = new GameObject("Interaction Playground UI", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(900, 680); scaler.matchWidthOrHeight = .5f;
            var panel = new GameObject("Controls", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24, 24); rect.offsetMax = new Vector2(-24, -24);
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(.07f, .09f, .13f, .98f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14); layout.spacing = 8;
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            Label(panel.transform, "ZeroEngine Interaction Playground", 36);
            Label(panel.transform, "Two actors • explicit input • no game assets or saves", 30);
            status = Label(panel.transform, "", 70);
            var actorRow = Row(panel.transform, "Actors");
            Button(actorRow, "Actor A", () => SelectActor(0)); Button(actorRow, "Actor B", () => SelectActor(1));
            Button(panel.transform, "Approach door — tap to toggle", () => Approach("door"));
            Button(panel.transform, "Approach NPC — tap to talk / hold to gift (1 coin)", () => Approach("npc"));
            Button(panel.transform, "Approach pickup — collect once per activation", () => Approach("pickup"));
            Button(panel.transform, "Approach shop — confirm a purchase (2 coins)", () => Approach("shop"));
            var interact = Button(panel.transform, "Interact — press / hold / release", null);
            InteractButton = interact.gameObject.AddComponent<SampleHoldButton>(); InteractButton.ResolveActor = () => SelectedActor;
            var controls = Row(panel.transform, "Lifecycle");
            Button(controls, "Leave range", LeaveRange); Button(controls, "Respawn pickup", RespawnPickup);
            var confirmation = Row(panel.transform, "Confirmation");
            confirm = Button(confirmation, "Confirm shop", () => ConfirmPurchase()); Button(confirmation, "Cancel", CancelPurchase);
            outcome = Label(panel.transform, lastOutcome, 54);
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events = Child("Playground EventSystem", transform);
                events.AddComponent<EventSystem>(); events.AddComponent<StandaloneInputModule>();
            }
        }

        private UnityEngine.UI.Text Label(Transform parent, string value, float height)
        {
            var node = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text), typeof(UnityEngine.UI.LayoutElement));
            node.transform.SetParent(parent, false); var label = node.GetComponent<UnityEngine.UI.Text>();
            label.font = font; label.fontSize = 20; label.color = Color.white; label.text = value;
            label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
            node.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;
            return label;
        }
        private UnityEngine.UI.Button Button(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var node = new GameObject(text, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
            node.transform.SetParent(parent, false); node.GetComponent<UnityEngine.UI.Image>().color = new Color(.18f, .26f, .38f);
            var element = node.GetComponent<UnityEngine.UI.LayoutElement>(); element.preferredHeight = 42; element.flexibleWidth = 1;
            var button = node.GetComponent<UnityEngine.UI.Button>(); if (action != null) button.onClick.AddListener(action);
            var label = Label(node.transform, text, 42); label.alignment = TextAnchor.MiddleCenter;
            var rect = label.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8, 0); rect.offsetMax = new Vector2(-8, 0);
            return button;
        }
        private static Transform Row(Transform parent, string name)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.HorizontalLayoutGroup), typeof(UnityEngine.UI.LayoutElement));
            node.transform.SetParent(parent, false); node.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 42;
            var layout = node.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>(); layout.spacing = 8;
            layout.childControlWidth = true; layout.childForceExpandWidth = true;
            return node.transform;
        }
        private static GameObject Child(string name, Transform parent)
        { var node = new GameObject(name); node.transform.SetParent(parent, false); return node; }
    }

    public sealed class SampleHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Func<InteractionAgentBehaviour> ResolveActor;
        private InteractionAgentBehaviour pressedOwner;
        public void OnPointerDown(PointerEventData data) { pressedOwner = ResolveActor?.Invoke(); pressedOwner?.Press(); }
        public void OnPointerUp(PointerEventData data) { pressedOwner?.Release(); pressedOwner = null; }
        public void OnPointerExit(PointerEventData data) { pressedOwner?.Cancel(); pressedOwner = null; }
        private void OnDisable() { pressedOwner?.Cancel(); pressedOwner = null; }
    }
}
