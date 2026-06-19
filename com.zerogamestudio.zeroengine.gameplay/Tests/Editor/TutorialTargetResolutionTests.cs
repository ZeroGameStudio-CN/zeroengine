using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Tutorial;

namespace ZeroEngine.Gameplay.Editor.Tests
{
    public sealed class TutorialTargetResolutionTests
    {
        [Test]
        public void FindUITargetReturnsRegisteredRectTransform()
        {
            var target = new GameObject("RegisteredTarget", typeof(RectTransform));
            try
            {
                var context = new TutorialContext();
                context.RegisterTarget("main_button", target);

                var resolved = context.FindUITarget("main_button");

                Assert.AreSame(target.GetComponent<RectTransform>(), resolved);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void MoveToStepUsesRegisteredTargetInsteadOfSceneNameLookup()
        {
            var player = new GameObject("Player");
            var registeredTarget = new GameObject("RegisteredMoveTarget");
            try
            {
                player.transform.position = new Vector3(5f, 0f, 0f);
                registeredTarget.transform.position = player.transform.position;

                var context = new TutorialContext { Player = player };
                context.RegisterTarget("move_target", registeredTarget);
                var step = new MoveToStep
                {
                    TargetObjectPath = "move_target",
                    TargetPosition = new Vector3(100f, 0f, 0f),
                    ArrivalDistance = 0.5f,
                    ArrivalDelay = 0f,
                    ShowArrow = false,
                    ShowOnMinimap = false
                };

                step.OnEnter(context);
                step.OnUpdate(context);

                Assert.IsTrue(step.IsCompleted(context));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(registeredTarget);
            }
        }

        [Test]
        public void MoveToStepDoesNotResolveUnregisteredSceneObjectName()
        {
            var player = new GameObject("Player");
            var unregisteredTarget = new GameObject("unregistered_target");
            try
            {
                player.transform.position = new Vector3(5f, 0f, 0f);
                unregisteredTarget.transform.position = player.transform.position;

                var context = new TutorialContext { Player = player };
                var step = new MoveToStep
                {
                    TargetObjectPath = "unregistered_target",
                    TargetPosition = new Vector3(100f, 0f, 0f),
                    ArrivalDistance = 0.5f,
                    ArrivalDelay = 0f,
                    ShowArrow = false,
                    ShowOnMinimap = false
                };

                step.OnEnter(context);
                step.OnUpdate(context);

                Assert.IsFalse(step.IsCompleted(context));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(unregisteredTarget);
            }
        }
    }
}
