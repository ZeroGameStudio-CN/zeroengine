using System;
using UnityEngine;

namespace ZeroEngine.Render.Tests.Runtime
{
    public sealed class BodyVisualBoundsGeometryDriver : MonoBehaviour
    {
        public Action Mutation;
        public bool Completed { get; private set; }
        public Exception Failure { get; private set; }

        private void Update()
        {
            if (Mutation == null || Completed) return;
            var action = Mutation;
            Mutation = null;
            try { action(); }
            catch (Exception error) { Failure = error; }
            finally { Completed = true; }
        }
    }
}
