using UnityEngine;
using UnityEngine.Playables;

namespace ZeroEngine.Cinematic
{
    public sealed class CinematicPlayableDirectorAdapter
    {
        private CinematicPlaybackContext _activeContext;
        private CinematicProjectPlaybackServices _activeServices = CinematicProjectPlaybackServices.None;
        private ICinematicCommandExecutor _activeCommandExecutor;
        private bool _hasActivePlayback;

        public CinematicPlayResult Play(
            CinematicSequenceDefinition sequence,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry)
        {
            return Play(
                CinematicPlayRequest.FromSequence(sequence),
                sequence,
                director,
                bindingRegistry,
                CinematicProjectPlaybackServices.None,
                null);
        }

        public CinematicPlayResult Play(
            CinematicPlayRequest request,
            CinematicSequenceDefinition sequence,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry,
            CinematicProjectPlaybackServices projectServices)
        {
            return Play(request, sequence, director, bindingRegistry, projectServices, null);
        }

        public CinematicPlayResult Play(
            CinematicPlayRequest request,
            CinematicSequenceDefinition sequence,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry,
            CinematicProjectPlaybackServices projectServices,
            ICinematicCommandExecutor commandExecutor)
        {
            if (sequence == null)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.SequenceMissing,
                    "Cinematic sequence is missing.",
                    sequenceId: request.SequenceId);
            }

            if (_hasActivePlayback)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.AlreadyPlaying,
                    $"Cinematic sequence '{_activeContext.Request.SequenceId}' is already playing.",
                    sequenceId: _activeContext.Request.SequenceId);
            }

            if (request.SequenceId != sequence.SequenceId)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.Failed,
                    $"Cinematic request '{request.SequenceId}' does not match sequence '{sequence.SequenceId}'.",
                    sequenceId: request.SequenceId);
            }

            if (director == null)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.Failed,
                    $"Cinematic sequence '{sequence.SequenceId}' has no PlayableDirector.",
                    sequenceId: sequence.SequenceId);
            }

            if (sequence.TimelineAsset == null)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.Failed,
                    $"Cinematic sequence '{sequence.SequenceId}' has no TimelineAsset.",
                    sequenceId: sequence.SequenceId);
            }

            var registry = bindingRegistry ?? new CinematicBindingRegistry();
            var bindingRequirements = sequence.BindingRequirements;
            for (var i = 0; i < bindingRequirements.Count; i++)
            {
                var requirement = bindingRequirements[i];
                if (!registry.TryResolve(requirement.BindingKey, out var binding))
                {
                    return new CinematicPlayResult(
                        CinematicPlayStatus.BindingMissing,
                        $"Cinematic binding '{requirement.BindingKey}' is missing.",
                        sequenceId: sequence.SequenceId);
                }
            }

            var context = new CinematicPlaybackContext(request, sequence);
            var services = projectServices ?? CinematicProjectPlaybackServices.None;
            var enterResult = services.Enter(context);
            if (enterResult.Status != CinematicPlayStatus.None)
            {
                return enterResult;
            }

            director.playableAsset = sequence.TimelineAsset;
            for (var i = 0; i < bindingRequirements.Count; i++)
            {
                var requirement = bindingRequirements[i];
                if (requirement.Track != null && registry.TryResolve(requirement.BindingKey, out var binding))
                {
                    director.SetGenericBinding(requirement.Track, binding);
                }
            }

            _activeContext = context;
            _activeServices = services;
            _activeCommandExecutor = commandExecutor;
            _hasActivePlayback = true;

            var startCommandResult = ExecuteCommands(CinematicCommandPhase.OnStart, sequence, context, commandExecutor);
            if (!startCommandResult.Succeeded)
            {
                var failedResult = new CinematicPlayResult(
                    CinematicPlayStatus.Failed,
                    startCommandResult.Message,
                    requiresAbortCleanup: true,
                    sequenceId: sequence.SequenceId);
                return StopWithResult(director, failedResult);
            }

            director.Play();

            return new CinematicPlayResult(
                CinematicPlayStatus.Started,
                $"Cinematic sequence '{sequence.SequenceId}' started.",
                sequenceId: sequence.SequenceId);
        }

        public CinematicPlayResult Stop(PlayableDirector director, CinematicPlayStatus terminalStatus)
        {
            if (!_hasActivePlayback)
            {
                return CinematicPlayResult.None;
            }

            var result = CreateTerminalResult(terminalStatus, _activeContext.Request.SequenceId);
            return StopWithResult(director, result);
        }

        public CinematicPlayResult EvaluateTimeout(PlayableDirector director, float elapsedSeconds)
        {
            if (!_hasActivePlayback)
            {
                return CinematicPlayResult.None;
            }

            var isPlaying = director != null && director.state == PlayState.Playing;
            var watchdog = new CinematicPlaybackWatchdog(_activeContext.Request.TimeoutPolicy);
            var result = watchdog.Evaluate(elapsedSeconds, isPlaying);
            if (result.Status == CinematicPlayStatus.None)
            {
                return result;
            }

            return StopWithResult(director, result.WithSequenceId(_activeContext.Request.SequenceId));
        }

        private CinematicPlayResult StopWithResult(PlayableDirector director, CinematicPlayResult result)
        {
            if (director != null)
            {
                director.Stop();
            }

            if (_hasActivePlayback)
            {
                var terminalCommandPhase = GetTerminalCommandPhase(result.Status, _activeContext.Sequence);
                var commandResult = ExecuteCommands(
                    terminalCommandPhase,
                    _activeContext.Sequence,
                    _activeContext,
                    _activeCommandExecutor);
                if (!commandResult.Succeeded)
                {
                    result = new CinematicPlayResult(
                        CinematicPlayStatus.Failed,
                        commandResult.Message,
                        requiresAbortCleanup: true,
                        sequenceId: _activeContext.Request.SequenceId);
                    if (terminalCommandPhase != CinematicCommandPhase.OnAbort)
                    {
                        var abortCommandResult = ExecuteCommands(
                            CinematicCommandPhase.OnAbort,
                            _activeContext.Sequence,
                            _activeContext,
                            _activeCommandExecutor);
                        if (!abortCommandResult.Succeeded)
                        {
                            result = new CinematicPlayResult(
                                CinematicPlayStatus.Failed,
                                abortCommandResult.Message,
                                requiresAbortCleanup: true,
                                sequenceId: _activeContext.Request.SequenceId);
                        }
                    }
                }
            }

            if (_hasActivePlayback)
            {
                result = _activeServices.Exit(_activeContext, result);
            }

            ClearActivePlayback();
            return result;
        }

        private void ClearActivePlayback()
        {
            _activeContext = default;
            _activeServices = CinematicProjectPlaybackServices.None;
            _activeCommandExecutor = null;
            _hasActivePlayback = false;
        }

        private static CinematicPlayResult CreateTerminalResult(
            CinematicPlayStatus terminalStatus,
            string sequenceId)
        {
            if (terminalStatus == CinematicPlayStatus.TimedOut)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.TimedOut,
                    "Cinematic playback timed out.",
                    requiresAbortCleanup: true,
                    sequenceId: sequenceId);
            }

            return new CinematicPlayResult(terminalStatus, sequenceId: sequenceId);
        }

        private static CinematicCommandPhase GetTerminalCommandPhase(
            CinematicPlayStatus status,
            CinematicSequenceDefinition sequence)
        {
            if (status == CinematicPlayStatus.Completed)
            {
                return CinematicCommandPhase.OnComplete;
            }

            if (status == CinematicPlayStatus.SkippedCompleted)
            {
                if (!HasCommands(sequence, CinematicCommandPhase.OnSkipped))
                {
                    return CinematicCommandPhase.OnComplete;
                }

                return CinematicCommandPhase.OnSkipped;
            }

            return CinematicCommandPhase.OnAbort;
        }

        private static bool HasCommands(
            CinematicSequenceDefinition sequence,
            CinematicCommandPhase phase)
        {
            var commands = sequence.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                if (commands[i].Phase == phase)
                {
                    return true;
                }
            }

            return false;
        }

        private static CinematicCommandResult ExecuteCommands(
            CinematicCommandPhase phase,
            CinematicSequenceDefinition sequence,
            CinematicPlaybackContext context,
            ICinematicCommandExecutor commandExecutor)
        {
            var commands = sequence.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (command.Phase != phase)
                {
                    continue;
                }

                if (commandExecutor == null)
                {
                    return CinematicCommandResult.Fail(
                        $"Cinematic command '{command.CommandId}' has no executor.");
                }

                CinematicCommandResult result;
                try
                {
                    result = commandExecutor.Execute(command, context);
                }
                catch (System.Exception exception)
                {
                    return CinematicCommandResult.Fail(
                        $"Cinematic command '{command.CommandId}' failed: {exception.Message}");
                }

                if (!result.Succeeded)
                {
                    return result;
                }
            }

            return CinematicCommandResult.Success();
        }
    }
}
