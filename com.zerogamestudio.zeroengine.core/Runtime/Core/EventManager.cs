using System;
using System.Collections.Generic;

namespace ZeroEngine.Core
{
    /// <summary>
    /// Legacy string event system retained for compatibility.
    /// New framework and project code should prefer ZeroEngine.Events.IEventBus with typed IGameEvent payloads.
    /// </summary>
    public static class EventManager
    {
        private static Dictionary<string, Delegate> _eventTable = new();

        #region No Arguments

        public static void Subscribe(string eventName, Action listener)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                _eventTable[eventName] = Delegate.Combine(existingDelegate, listener);
            }
            else
            {
                _eventTable[eventName] = listener;
            }
        }

        public static void Unsubscribe(string eventName, Action listener)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                var newDelegate = Delegate.Remove(existingDelegate, listener);
                if (newDelegate == null)
                    _eventTable.Remove(eventName);
                else
                    _eventTable[eventName] = newDelegate;
            }
        }

        public static void Trigger(string eventName)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                (existingDelegate as Action)?.Invoke();
            }
        }

        #endregion

        #region Single Argument

        public static void Subscribe<T>(string eventName, Action<T> listener)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                _eventTable[eventName] = Delegate.Combine(existingDelegate, listener);
            }
            else
            {
                _eventTable[eventName] = listener;
            }
        }

        public static void Unsubscribe<T>(string eventName, Action<T> listener)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                var newDelegate = Delegate.Remove(existingDelegate, listener);
                if (newDelegate == null)
                    _eventTable.Remove(eventName);
                else
                    _eventTable[eventName] = newDelegate;
            }
        }

        public static void Trigger<T>(string eventName, T arg)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                (existingDelegate as Action<T>)?.Invoke(arg);
            }
        }

        #endregion

        #region Two Arguments

        public static void Subscribe<T1, T2>(string eventName, Action<T1, T2> listener)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                _eventTable[eventName] = Delegate.Combine(existingDelegate, listener);
            }
            else
            {
                _eventTable[eventName] = listener;
            }
        }

        public static void Unsubscribe<T1, T2>(string eventName, Action<T1, T2> listener)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                var newDelegate = Delegate.Remove(existingDelegate, listener);
                if (newDelegate == null)
                    _eventTable.Remove(eventName);
                else
                    _eventTable[eventName] = newDelegate;
            }
        }

        public static void Trigger<T1, T2>(string eventName, T1 arg1, T2 arg2)
        {
            if (_eventTable.TryGetValue(eventName, out var existingDelegate))
            {
                (existingDelegate as Action<T1, T2>)?.Invoke(arg1, arg2);
            }
        }

        #endregion

        /// <summary>
        /// Clears all events (Call on Scene Change)
        /// </summary>
        public static void Clear()
        {
            _eventTable.Clear();
        }
    }

    /// <summary>
    /// Core Game Event Constants
    /// </summary>
    public static class GameEvents
    {
        // Core
        public const string GameStarted = "Game.Started";
        public const string GamePaused = "Game.Paused";
        public const string GameResumed = "Game.Resumed";
        public const string SceneLoaded = "System.SceneLoaded";

        // Battle
        public const string BattleStart = "Battle.Start";
        public const string BattleEnd = "Battle.End";
        public const string TurnStart = "Battle.TurnStart";
        public const string TurnEnd = "Battle.TurnEnd";
        
        // Character
        public const string PlayerSpawned = "Character.PlayerSpawned";
        public const string CharacterDied = "Character.Died";
        public const string ExpGained = "Character.ExpGained";
        public const string CurrencyGained = "Character.CurrencyGained";  // v1.2.0+
        public const string CurrencySpent = "Character.CurrencySpent";    // v1.9.0+

        // Quest
        public const string QuestAccepted = "Quest.Accepted";
        public const string QuestCompleted = "Quest.Completed";
        public const string QuestFailed = "Quest.Failed";
        public const string QuestProgressChanged = "Quest.ProgressChanged";

        // Inventory
        public const string ItemObtained = "Item.Obtained";
        public const string ItemRemoved = "Item.Removed";
        public const string InventoryUpdated = "Inventory.Updated";
        
        // Network
        public const string NetworkConnected = "Network.Connected";
        public const string NetworkDisconnected = "Network.Disconnected";
        public const string NetworkPlayerJoined = "Network.PlayerJoined";
        public const string NetworkPlayerLeft = "Network.PlayerLeft";
    }
}

