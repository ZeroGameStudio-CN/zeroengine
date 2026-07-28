using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.PlayerSettings
{
    public readonly struct DisplayState
    {
        public DisplayState(
            FullScreenMode windowMode,
            int width,
            int height,
            int refreshRate,
            int vSyncCount,
            int frameRateLimit,
            string qualityName)
        {
            WindowMode = windowMode;
            Width = width;
            Height = height;
            RefreshRate = refreshRate;
            VSyncCount = vSyncCount;
            FrameRateLimit = frameRateLimit;
            QualityName = qualityName;
        }

        public FullScreenMode WindowMode { get; }
        public int Width { get; }
        public int Height { get; }
        public int RefreshRate { get; }
        public int VSyncCount { get; }
        public int FrameRateLimit { get; }
        public string QualityName { get; }
    }

    public interface IDisplaySettingsDriver
    {
        DisplayState Capture();
        SettingApplyResult Apply(DisplayState state);
    }

    public sealed class UnityDisplaySettingsDriver : IDisplaySettingsDriver
    {
        public DisplayState Capture()
        {
            var refresh = Screen.currentResolution.refreshRateRatio.value;
            return new DisplayState(
                Screen.fullScreenMode,
                Screen.width,
                Screen.height,
                Mathf.RoundToInt((float)refresh),
                QualitySettings.vSyncCount,
                Application.targetFrameRate,
                QualitySettings.names[QualitySettings.GetQualityLevel()]);
        }

        public SettingApplyResult Apply(DisplayState state)
        {
            try
            {
                var qualityIndex = Array.IndexOf(QualitySettings.names, state.QualityName);
                if (qualityIndex >= 0)
                {
                    QualitySettings.SetQualityLevel(qualityIndex, true);
                }

                QualitySettings.vSyncCount = state.VSyncCount;
                Application.targetFrameRate = state.VSyncCount > 0 ? -1 : state.FrameRateLimit;
                var refresh = ResolveRefreshRate(state);
                Screen.SetResolution(state.Width, state.Height, state.WindowMode, refresh);
                return SettingApplyResult.Applied();
            }
            catch (Exception exception)
            {
                return SettingApplyResult.Failed(exception.GetType().Name);
            }
        }

        private static RefreshRate ResolveRefreshRate(DisplayState state)
        {
            var candidates = Screen.resolutions.Where(x => x.width == state.Width && x.height == state.Height).ToArray();
            if (candidates.Length == 0 || state.RefreshRate <= 0)
            {
                return new RefreshRate { numerator = 0, denominator = 1 };
            }

            return candidates.OrderBy(x => Math.Abs(x.refreshRateRatio.value - state.RefreshRate))
                .First().refreshRateRatio;
        }
    }

    public sealed class DisplaySettingsApplier : ISettingApplier
    {
        private static readonly SettingId[] Ids =
        {
            StandardSettingIds.WindowMode, StandardSettingIds.Width, StandardSettingIds.Height,
            StandardSettingIds.RefreshRate, StandardSettingIds.VSyncCount,
            StandardSettingIds.FrameRateLimit, StandardSettingIds.Quality
        };
        private readonly IDisplaySettingsDriver _driver;

        public DisplaySettingsApplier(IDisplaySettingsDriver driver = null)
        {
            _driver = driver ?? new UnityDisplaySettingsDriver();
        }

        public IReadOnlyCollection<SettingId> SettingIds => Ids;
        public DisplayState Capture() => _driver.Capture();
        public SettingApplyResult Restore(DisplayState state) => _driver.Apply(state);

        public Task<SettingApplyResult> ApplyAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (!Enum.TryParse(snapshot[StandardSettingIds.WindowMode].AsString(), out FullScreenMode mode))
            {
                return Task.FromResult(SettingApplyResult.Failed("window-mode-invalid"));
            }

            var state = new DisplayState(
                mode,
                snapshot[StandardSettingIds.Width].AsInt(),
                snapshot[StandardSettingIds.Height].AsInt(),
                snapshot[StandardSettingIds.RefreshRate].AsInt(),
                snapshot[StandardSettingIds.VSyncCount].AsInt(),
                snapshot[StandardSettingIds.FrameRateLimit].AsInt(),
                snapshot[StandardSettingIds.Quality].AsString());
            return Task.FromResult(_driver.Apply(state));
        }
    }

    public sealed class DisplayPreviewConfirmation
    {
        private readonly DisplaySettingsApplier _applier;
        private DisplayState _previous;

        public DisplayPreviewConfirmation(DisplaySettingsApplier applier, TimeSpan? timeout = null)
        {
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            Timeout = timeout ?? TimeSpan.FromSeconds(15);
        }

        public TimeSpan Timeout { get; }
        public bool IsPending { get; private set; }
        public DateTimeOffset Deadline { get; private set; }

        public void Begin(DateTimeOffset now)
        {
            if (!IsPending)
            {
                _previous = _applier.Capture();
            }

            IsPending = true;
            Deadline = now + Timeout;
        }

        public void Confirm() => IsPending = false;

        public bool Update(DateTimeOffset now)
        {
            if (!IsPending || now < Deadline)
            {
                return false;
            }

            _applier.Restore(_previous);
            IsPending = false;
            return true;
        }

        public void Cancel()
        {
            if (!IsPending)
            {
                return;
            }

            _applier.Restore(_previous);
            IsPending = false;
        }
    }
}
