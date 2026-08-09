using System;
using System.Collections;
using System.Reflection;

namespace ZeroEngine.TCE.Editor
{
    public static class TceGraphLaneModel
    {
        public static void AddComponent(TceGraph graph, TceGraphLane lane, TceComponentData data)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!CanAddToLane(lane, data))
                throw new ArgumentException($"{data.GetType().Name} cannot be added to {lane}.", nameof(data));

            switch (lane)
            {
                case TceGraphLane.Trigger:
                    graph.AddTrigger((TceTriggerData)data);
                    break;
                case TceGraphLane.Condition:
                    graph.AddCondition((TceConditionData)data);
                    break;
                case TceGraphLane.Effect:
                    graph.AddEffect((TceEffectData)data);
                    break;
            }
        }

        public static bool CanAddToLane(TceGraphLane lane, TceComponentData data)
        {
            return lane switch
            {
                TceGraphLane.Trigger => data is TceTriggerData,
                TceGraphLane.Condition => data is TceConditionData,
                TceGraphLane.Effect => data is TceEffectData,
                _ => false
            };
        }

        public static int Count(TceGraph graph, TceGraphLane lane)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            return GetMutableLane(graph, lane).Count;
        }

        public static TceComponentData GetComponent(TceGraph graph, TceGraphLane lane, int index)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            return (TceComponentData)GetMutableLane(graph, lane)[index];
        }

        public static void Remove(TceGraph graph, TceGraphLane lane, int index)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            GetMutableLane(graph, lane).RemoveAt(index);
        }

        public static void Move(TceGraph graph, TceGraphLane lane, int fromIndex, int toIndex)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            IList list = GetMutableLane(graph, lane);
            object item = list[fromIndex];
            list.RemoveAt(fromIndex);
            list.Insert(toIndex, item);
        }

        private static IList GetMutableLane(TceGraph graph, TceGraphLane lane)
        {
            string fieldName = lane switch
            {
                TceGraphLane.Trigger => "triggers",
                TceGraphLane.Condition => "conditions",
                TceGraphLane.Effect => "effects",
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null)
            };

            FieldInfo field = typeof(TceGraph).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new InvalidOperationException($"TceGraph private field '{fieldName}' was not found.");

            return (IList)field.GetValue(graph);
        }
    }
}
