using System;
using IndependentContent.Example;
using ZeroEngine.Interaction.Core;

namespace ZeroEngine.Interaction.Samples.ExternalModule
{
    /// <summary>Example thin adapter only. The project supplies current conditions and its business transaction.</summary>
    public sealed class ModuleInteractionAdapter : IInteractionTarget
    {
        private readonly Func<InteractionContext, InteractionCheck> admission;
        private readonly Func<InteractionContext, InteractionCheck> commit;
        public string Id { get; }
        public int Priority => 0;
        public bool IsAvailable => true;
        public InteractionPolicy Policy => InteractionPolicy.Press;
        public IndependentModule Module { get; private set; }
        public IInteractionExecution Execution { get; private set; }
        public ModuleInteractionAdapter(string id, Func<InteractionContext, InteractionCheck> admission, Func<InteractionContext, InteractionCheck> commit)
        {
            Id = id; this.admission = admission ?? throw new ArgumentNullException(nameof(admission));
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }
        public InteractionCheck Check(InteractionContext context) => admission(context);
        public void Execute(InteractionRequest request)
        {
            IInteractionExecution execution = request;
            var module = new IndependentModule(); Execution = execution; Module = module;
            // Capture this exact execution, rather than a mutable current-host field, in delayed callbacks.
            execution.Settled += _ => module.Stop();
            try
            {
                module.Begin(execution.LifetimeToken, accepted =>
                {
                    if (!accepted) { execution.TryDeny("Content declined completion."); return; }
                    execution.TryCommitResult(() => commit(execution.Context));
                });
            }
            catch (Exception error) { execution.TryFault(error); }
        }
    }
}
