using Maze.Core.Common;
using UnityEngine;

namespace Maze.Presentation.Visual
{
    /// <summary>
    /// Head of a character view (breath puffs): the humanoid head bone, found once; without one — the model's root
    /// (<see cref="FallbackHeight"/> above it). <see cref="View"/> is null for the player (always shown).
    /// </summary>
    public readonly struct CharacterHead
    {
        public const float FallbackHeight = 1.6f;

        public readonly string Id;
        public readonly EntityView View;
        public readonly Transform Root;
        public readonly Transform Head;

        /// <summary>0..1 from the id: characters standing together do not breathe in sync.</summary>
        public readonly float Phase;

        private CharacterHead(string id, EntityView view, Transform root, Transform head)
        {
            Id = id;
            View = view;
            Root = root;
            Head = head;
            Phase = (StableHash.Of(id ?? string.Empty) % 1000UL) / 1000f;
        }

        public bool IsShown => Root != null && (View == null || View.IsVisible);

        public static CharacterHead Of(string id, EntityView view, Animator animator) =>
            Of(id, view, view?.GameObject, animator);

        public static CharacterHead Of(string id, EntityView view, GameObject root, Animator animator)
        {
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            return new CharacterHead(id, view, root != null ? root.transform : null, head);
        }

        /// <summary>The mouth: forward and down from the head in the direction the model faces.</summary>
        public Vector3 Mouth(Vector2 offset)
        {
            var forward = Root.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            var head = Head != null ? Head.position : Root.position + Vector3.up * FallbackHeight;
            return head + forward * offset.x + Vector3.up * offset.y;
        }
    }
}
