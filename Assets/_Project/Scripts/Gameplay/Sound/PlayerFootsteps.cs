using Maze.Gameplay.Level;
using Maze.Gameplay.Player;

namespace Maze.Gameplay.Sound
{
    /// <summary>
    /// Step sounds (ТЗ §71): a periodic event while the player moves — one every
    /// <see cref="Core.Definitions.PlayerDefinition.StepDistance"/> cells walked. Standing still makes no sound.
    /// Must be registered after <see cref="PlayerSystem"/>.
    /// </summary>
    public sealed class PlayerFootsteps : ILevelTickable
    {
        private readonly PlayerSystem _player;
        private readonly SoundEventBus _sounds;
        private UnityEngine.Vector2 _last;
        private float _walked;
        private bool _started;

        public PlayerFootsteps(PlayerSystem player, SoundEventBus sounds)
        {
            _player = player;
            _sounds = sounds;
        }

        public void Tick(float deltaTime)
        {
            if (!_player.IsSpawned)
                return;

            var position = _player.Position;
            if (!_started)
            {
                _last = position;
                _started = true;
                return;
            }

            _walked += (position - _last).magnitude;
            _last = position;

            var definition = _player.Definition;
            if (_walked < definition.StepDistance)
                return;

            _walked -= definition.StepDistance;
            if (_walked > definition.StepDistance) _walked = 0f; // a teleport is not a run of steps
            _sounds.Emit(SoundType.Step, position, definition.StepSoundRadius);
        }
    }
}
