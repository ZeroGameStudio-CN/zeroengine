#if UNITASK
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif

namespace ZeroEngine.Command
{
    public class WaitCommand : ICommand
    {
        private readonly float _duration;

        public WaitCommand(float duration)
        {
            _duration = duration;
        }

#if UNITASK
        public async UniTask Execute()
#else
        public async Task Execute()
#endif
        {
             int delayMs = (int)(_duration * 1000);
#if UNITASK
             await UniTask.Delay(delayMs);
#else
             await Task.Delay(delayMs);
#endif
        }

#if UNITASK
        public async UniTask Undo()
#else
        public async Task Undo()
#endif
        {
            // Cannot undo time, but satisfied interface
#if UNITASK
            await UniTask.CompletedTask;
#else
            await Task.CompletedTask;
#endif
        }
    }
}
