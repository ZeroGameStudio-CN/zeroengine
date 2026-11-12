using System.Collections.Generic;

namespace ZeroEngine.Events
{
    public sealed class EventBusRegistry
    {
        private readonly Dictionary<EventScope, IEventBus> _buses = new();

        public IEventBus Get(EventScope scope)
        {
            if (!_buses.TryGetValue(scope, out var bus))
            {
                bus = new EventBus();
                _buses[scope] = bus;
            }

            return bus;
        }

        public bool TryGet(EventScope scope, out IEventBus bus)
        {
            return _buses.TryGetValue(scope, out bus);
        }

        public void Set(EventScope scope, IEventBus bus)
        {
            if (bus == null)
            {
                _buses.Remove(scope);
            }
            else
            {
                _buses[scope] = bus;
            }
        }

        public IEventBus Reset(EventScope scope)
        {
            var bus = new EventBus();
            _buses[scope] = bus;
            return bus;
        }

        public void Clear()
        {
            _buses.Clear();
        }
    }
}
