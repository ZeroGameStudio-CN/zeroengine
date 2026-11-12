using UnityEngine.Playables;

namespace ZeroEngine.Cinematic
{
    public sealed class CinematicPlaybackService
    {
        private readonly ICinematicSequenceResolver _sequenceResolver;
        private readonly PlayableDirector _director;
        private readonly CinematicBindingRegistry _bindingRegistry;
        private readonly CinematicProjectPlaybackServices _projectServices;
        private readonly ICinematicCommandExecutor _commandExecutor;
        private readonly CinematicPlayableDirectorAdapter _directorAdapter;
        private CinematicPlayRequest _activeRequest;
        private CinematicSequenceDefinition _activeSequence;
        private float _activeElapsedSeconds;
        private bool _hasActivePlayback;

        public CinematicPlaybackService(
            CinematicSequenceCatalog catalog,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry,
            CinematicProjectPlaybackServices projectServices,
            ICinematicCommandExecutor commandExecutor)
            : this(
                (ICinematicSequenceResolver)catalog,
                director,
                bindingRegistry,
                projectServices,
                commandExecutor,
                new CinematicPlayableDirectorAdapter())
        {
        }

        public CinematicPlaybackService(
            CinematicSequenceCatalog catalog,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry,
            CinematicProjectPlaybackServices projectServices,
            ICinematicCommandExecutor commandExecutor,
            CinematicPlayableDirectorAdapter directorAdapter)
            : this(
                (ICinematicSequenceResolver)catalog,
                director,
                bindingRegistry,
                projectServices,
                commandExecutor,
                directorAdapter)
        {
        }

        public CinematicPlaybackService(
            ICinematicSequenceResolver sequenceResolver,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry,
            CinematicProjectPlaybackServices projectServices,
            ICinematicCommandExecutor commandExecutor)
            : this(
                sequenceResolver,
                director,
                bindingRegistry,
                projectServices,
                commandExecutor,
                new CinematicPlayableDirectorAdapter())
        {
        }

        public CinematicPlaybackService(
            ICinematicSequenceResolver sequenceResolver,
            PlayableDirector director,
            CinematicBindingRegistry bindingRegistry,
            CinematicProjectPlaybackServices projectServices,
            ICinematicCommandExecutor commandExecutor,
            CinematicPlayableDirectorAdapter directorAdapter)
        {
            _sequenceResolver = sequenceResolver;
            _director = director;
            _bindingRegistry = bindingRegistry ?? new CinematicBindingRegistry();
            _projectServices = projectServices ?? CinematicProjectPlaybackServices.None;
            _commandExecutor = commandExecutor;
            _directorAdapter = directorAdapter ?? new CinematicPlayableDirectorAdapter();
        }

        public CinematicPlayResult Play(string sequenceId)
        {
            if (_hasActivePlayback)
            {
                return CreateAlreadyPlayingResult();
            }

            var requestedId = string.IsNullOrWhiteSpace(sequenceId) ? string.Empty : sequenceId.Trim();
            if (_sequenceResolver == null || !_sequenceResolver.TryResolve(requestedId, out var sequence))
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.SequenceMissing,
                    $"Cinematic sequence '{requestedId}' is missing.",
                    sequenceId: requestedId);
            }

            return Play(CinematicPlayRequest.FromSequence(sequence), sequence);
        }

        public CinematicPlayResult Play(CinematicPlayRequest request)
        {
            if (_hasActivePlayback && !request.AllowInterrupt)
            {
                return CreateAlreadyPlayingResult();
            }

            if (_sequenceResolver == null || !_sequenceResolver.TryResolve(request.SequenceId, out var sequence))
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.SequenceMissing,
                    $"Cinematic sequence '{request.SequenceId}' is missing.",
                    sequenceId: request.SequenceId);
            }

            if (_hasActivePlayback)
            {
                var interruptResult = Stop(CinematicPlayStatus.Cancelled);
                if (interruptResult.Status == CinematicPlayStatus.Failed)
                {
                    return interruptResult;
                }
            }

            var resolvedRequest = CinematicPlayRequest.FromSequence(
                sequence,
                request.SourceId,
                request.AllowInterrupt);
            return Play(resolvedRequest, sequence);
        }

        private CinematicPlayResult CreateAlreadyPlayingResult()
        {
            return new CinematicPlayResult(
                CinematicPlayStatus.AlreadyPlaying,
                $"Cinematic sequence '{_activeRequest.SequenceId}' is already playing.",
                sequenceId: _activeRequest.SequenceId);
        }

        public CinematicPlayResult Stop(CinematicPlayStatus terminalStatus)
        {
            var result = _directorAdapter.Stop(_director, terminalStatus);
            if (result.Status != CinematicPlayStatus.None &&
                result.Status != CinematicPlayStatus.SkipNotAllowed)
            {
                ClearActivePlayback();
            }

            return result;
        }

        public CinematicPlayResult Cancel()
        {
            return Stop(CinematicPlayStatus.Cancelled);
        }

        public CinematicPlayResult Abort()
        {
            return Stop(CinematicPlayStatus.Aborted);
        }

        public CinematicPlayResult EvaluateTimeout(float elapsedSeconds)
        {
            var result = _directorAdapter.EvaluateTimeout(_director, elapsedSeconds);
            if (result.Status != CinematicPlayStatus.None)
            {
                ClearActivePlayback();
            }

            return result;
        }

        public CinematicPlayResult Tick(float deltaSeconds)
        {
            if (!_hasActivePlayback)
            {
                return CinematicPlayResult.None;
            }

            if (deltaSeconds > 0f)
            {
                _activeElapsedSeconds += deltaSeconds;
            }

            if (_director == null)
            {
                return Stop(CinematicPlayStatus.Completed);
            }

            if (_director.state != PlayState.Playing)
            {
                if (_activeSequence != null &&
                    _activeElapsedSeconds < _activeSequence.MinimumPlaybackSeconds)
                {
                    return CinematicPlayResult.None;
                }

                return Stop(CinematicPlayStatus.Completed);
            }

            if (_director.duration > 0d &&
                _director.time >= _director.duration)
            {
                if (_activeSequence != null &&
                    _activeElapsedSeconds < _activeSequence.MinimumPlaybackSeconds)
                {
                    return CinematicPlayResult.None;
                }

                return Stop(CinematicPlayStatus.Completed);
            }

            return EvaluateTimeout(_activeElapsedSeconds);
        }

        public CinematicPlayResult Skip()
        {
            return Skip(_activeElapsedSeconds);
        }

        public CinematicPlayResult Skip(float elapsedSeconds)
        {
            if (!_hasActivePlayback)
            {
                return CinematicPlayResult.None;
            }

            if (_activeRequest.SkipPolicy == CinematicSkipPolicy.Disabled)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.SkipNotAllowed,
                    $"Cinematic sequence '{_activeRequest.SequenceId}' cannot be skipped.",
                    sequenceId: _activeRequest.SequenceId);
            }

            if (_activeRequest.SkipPolicy == CinematicSkipPolicy.Abort)
            {
                return Stop(CinematicPlayStatus.Aborted);
            }

            if (_activeRequest.SkipPolicy == CinematicSkipPolicy.AllowAfterMinimumPlayback &&
                elapsedSeconds < _activeSequence.MinimumPlaybackSeconds)
            {
                return new CinematicPlayResult(
                    CinematicPlayStatus.SkipNotAllowed,
                    $"Cinematic sequence '{_activeRequest.SequenceId}' cannot be skipped before minimum playback.",
                    sequenceId: _activeRequest.SequenceId);
            }

            return Stop(CinematicPlayStatus.SkippedCompleted);
        }

        private CinematicPlayResult Play(
            CinematicPlayRequest request,
            CinematicSequenceDefinition sequence)
        {
            var result = _directorAdapter.Play(
                request,
                sequence,
                _director,
                _bindingRegistry,
                _projectServices,
                _commandExecutor);
            if (result.Status == CinematicPlayStatus.Started)
            {
                _activeRequest = request;
                _activeSequence = sequence;
                _activeElapsedSeconds = 0f;
                _hasActivePlayback = true;
            }

            return result;
        }

        private void ClearActivePlayback()
        {
            _activeRequest = default;
            _activeSequence = null;
            _activeElapsedSeconds = 0f;
            _hasActivePlayback = false;
        }
    }
}
