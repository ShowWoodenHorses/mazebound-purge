using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Optional component of a door prefab: shows the state decided by DoorSystem (ТЗ §37 Closed/Open/Locked/
    /// Unlocked). Never decides anything itself. Any of the references may be empty.
    /// Without this component a door view only hides its renderers while open.
    /// </summary>
    public sealed class DoorVisual : MonoBehaviour
    {
        private static readonly int OpenParameter = Animator.StringToHash("Open");
        private static readonly int LockedParameter = Animator.StringToHash("Locked");

        [Tooltip("Shown while the door is closed (e.g. the door panel).")]
        [SerializeField] private GameObject _closed;

        [Tooltip("Shown while the door is open (e.g. the panel swung against the wall).")]
        [SerializeField] private GameObject _open;

        [Tooltip("Shown while the door is locked (e.g. a padlock).")]
        [SerializeField] private GameObject _locked;

        [Tooltip("Optional: receives bool parameters \"Open\" and \"Locked\" (if the controller has them).")]
        [SerializeField] private Animator _animator;

        public void SetState(bool open, bool locked)
        {
            if (_closed != null) _closed.SetActive(!open);
            if (_open != null) _open.SetActive(open);
            if (_locked != null) _locked.SetActive(locked && !open);

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var parameter in _animator.parameters)
                {
                    if (parameter.type != AnimatorControllerParameterType.Bool) continue;
                    if (parameter.nameHash == OpenParameter) _animator.SetBool(OpenParameter, open);
                    else if (parameter.nameHash == LockedParameter) _animator.SetBool(LockedParameter, locked);
                }
            }
        }
    }
}
