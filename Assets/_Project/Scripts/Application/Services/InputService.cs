using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Maze.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maze.Application.Services
{
    public interface IInputService
    {
        /// <summary>Escape / P / gamepad Start / Android Back (P: a fullscreen browser keeps Escape for itself).</summary>
        event Action PauseRequested;

        /// <summary>M / Tab / gamepad Select (also the HUD's map button on touch screens).</summary>
        event Action MapRequested;

        /// <summary>Enter / Space / gamepad South: closes a window waiting for "continue" (tutorial).</summary>
        event Action ConfirmRequested;
    }

    /// <summary>
    /// Input System (new only) wrapper with the input abstraction of ТЗ §64: Move, Look, Attack, Interact,
    /// SwitchMelee, SwitchRanged, Reload, OpenMap (+ Pause). Desktop: WASD/arrows, mouse, keys. Gamepad: sticks and buttons.
    /// Android: the HUD's on-screen stick and buttons drive the same gamepad controls.
    /// A mouse click that starts over the UI (the HUD's Pause / Map buttons, screens) is not an attack, for as long as
    /// that button stays down (<see cref="IUiPointer"/>; checked when gameplay polls the attack, not in input callbacks).
    /// </summary>
    public sealed class InputService : IInputService, IPlayerInput, IApplicationService, IDisposable
    {
        private InputActionMap _actions;
        private InputAction _move;
        private InputAction _look;
        private InputAction _attack;
        private InputAction _pointerAttack;
        private readonly InputAction[] _buttons = new InputAction[6];
        private readonly IUiPointer _uiPointer;
        private bool _pointerBlocked;
        private int _pointerCheckedFrame = -1;

        public InputService(IUiPointer uiPointer)
        {
            _uiPointer = uiPointer;
        }

        public string Name => "Input";

        public event Action PauseRequested;
        public event Action MapRequested;
        public event Action ConfirmRequested;

        public Vector2 Move => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;
        public bool LookIsPointer => _look?.activeControl?.device is Pointer;
        public bool AttackHeld
        {
            get
            {
                if (_attack == null) return false;
                if (_attack.IsPressed()) return true;
                UpdatePointerBlock();
                return _pointerAttack.IsPressed() && !_pointerBlocked;
            }
        }

        public bool WasPressed(PlayerAction action)
        {
            var button = _buttons[(int)action];
            if (button == null) return false;
            if (action != PlayerAction.Attack) return button.WasPerformedThisFrame();

            if (button.WasPerformedThisFrame()) return true;
            UpdatePointerBlock();
            return _pointerAttack.WasPerformedThisFrame() && !_pointerBlocked;
        }

        /// <summary>
        /// A mouse press decides once, on its first frame, whether it belongs to the UI; it stays so until released.
        /// </summary>
        private void UpdatePointerBlock()
        {
            if (_pointerAttack == null) return;
            if (_pointerAttack.WasPressedThisFrame())
            {
                var frame = Time.frameCount;
                if (_pointerCheckedFrame != frame)
                {
                    _pointerCheckedFrame = frame;
                    _pointerBlocked = _uiPointer != null && _uiPointer.IsOverUi;
                }
            }
            else if (!_pointerAttack.IsPressed())
            {
                _pointerBlocked = false;
            }
        }

        public UniTask InitializeAsync(CancellationToken cancellation)
        {
            _actions = new InputActionMap("Maze");

            _move = _actions.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick"); // also the on-screen stick

            _look = _actions.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            _look.AddBinding("<Pointer>/position");
            _look.AddBinding("<Gamepad>/rightStick");

            _attack = Button(PlayerAction.Attack, "<Keyboard>/space", "<Gamepad>/buttonSouth");
            // The mouse separately: its clicks may belong to the UI.
            _pointerAttack = _actions.AddAction("PointerAttack", InputActionType.Button);
            _pointerAttack.AddBinding("<Mouse>/leftButton");
            Button(PlayerAction.Interact, "<Keyboard>/e", "<Gamepad>/buttonWest");
            Button(PlayerAction.SwitchMelee, "<Keyboard>/1", "<Gamepad>/leftShoulder");
            Button(PlayerAction.SwitchRanged, "<Keyboard>/2", "<Gamepad>/rightShoulder");
            Button(PlayerAction.Reload, "<Keyboard>/r", "<Gamepad>/buttonNorth");
            // Map and pause also work while gameplay is paused (to close the map), so they are events
            // rather than polled by level ticks. Android Back arrives as Escape.
            // Keys are bound by their place (US layout), not by the letter: a letter binding ("#(m)") finds no key on
            // a Russian layout. On AZERTY the places are ZQSD for WASD (the French tutorial says so) and "," for M
            // (the French tutorial names Tab for the map).
            var map = Button(PlayerAction.OpenMap, "<Keyboard>/m", "<Keyboard>/tab", "<Gamepad>/select");
            map.performed += _ => MapRequested?.Invoke();

            var pause = _actions.AddAction("Pause", InputActionType.Button);
            pause.AddBinding("<Keyboard>/escape");
            pause.AddBinding("<Keyboard>/p"); // a fullscreen browser takes Escape to leave fullscreen
            pause.AddBinding("<Gamepad>/start");
            pause.performed += _ => PauseRequested?.Invoke();

            var confirm = _actions.AddAction("Confirm", InputActionType.Button);
            confirm.AddBinding("<Keyboard>/enter");
            confirm.AddBinding("<Keyboard>/numpadEnter");
            confirm.AddBinding("<Keyboard>/space");
            confirm.AddBinding("<Gamepad>/buttonSouth");
            confirm.performed += _ => ConfirmRequested?.Invoke();

            _actions.Enable();
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_actions == null) return;
            _actions.Disable();
            _actions.Dispose();
            _actions = null;
            _move = _look = _attack = _pointerAttack = null;
            _pointerBlocked = false;
            Array.Clear(_buttons, 0, _buttons.Length);
        }

        private InputAction Button(PlayerAction playerAction, params string[] bindings)
        {
            var action = _actions.AddAction(playerAction.ToString(), InputActionType.Button);
            foreach (var binding in bindings)
                action.AddBinding(binding);
            _buttons[(int)playerAction] = action;
            return action;
        }
    }
}
