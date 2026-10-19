using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace Purge.Application.Services
{
    /// <summary>State of <see cref="TouchAimDevice"/>: one stick.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct TouchAimState : IInputStateTypeInfo
    {
        [InputControl(name = "stick", layout = "Stick")]
        [FieldOffset(0)] public Vector2 Stick;

        public FourCC format => new FourCC('P', 'A', 'I', 'M');
    }

    /// <summary>
    /// The virtual device of the on-screen aim stick (two-stick scheme): its own layout, so no real device ever
    /// matches <see cref="ControlPath"/> — a gamepad's right stick only aims, the on-screen one also fires.
    /// Registered by <see cref="InputService"/>; the on-screen stick creates the device when it is enabled.
    /// </summary>
    [InputControlLayout(stateType = typeof(TouchAimState), displayName = "Purge Touch Aim")]
    public sealed class TouchAimDevice : InputDevice
    {
        public const string LayoutName = "PurgeTouchAim";
        public const string ControlPath = "<" + LayoutName + ">/stick";

        public StickControl Stick { get; private set; }

        /// <summary>Before any scene loads, so no on-screen stick is ever enabled without its layout (again by InputService: harmless).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Register() => InputSystem.RegisterLayout<TouchAimDevice>(LayoutName);

        protected override void FinishSetup()
        {
            Stick = GetChildControl<StickControl>("stick");
            base.FinishSetup();
        }
    }
}
