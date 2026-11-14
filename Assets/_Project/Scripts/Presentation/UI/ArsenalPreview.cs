using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace Purge.Presentation.UI
{
    /// <summary>Display only: an isolated camera and a drag-rotatable model, no gameplay or physics.</summary>
    public sealed class ArsenalPreview : MonoBehaviour, IDragHandler
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _pivot;
        [SerializeField] private RawImage _image;
        private RenderTexture _texture;
        private GameObject _model;
        public void Show(GameObject prefab)
        {
            Clear(); if (prefab == null) return;
            if (_texture == null) { _texture = new RenderTexture(640, 400, 16); _camera.targetTexture = _texture; _image.texture = _texture; }
            _camera.gameObject.SetActive(true);
            _pivot.localRotation = Quaternion.identity;
            _model = Instantiate(prefab, _pivot, false);
            _model.transform.localPosition = Vector3.zero; _model.transform.localRotation = Quaternion.identity;
            var renderers = _model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                _model.transform.position += _pivot.position - bounds.center;
                _camera.orthographicSize = Mathf.Max(0.2f, bounds.size.magnitude * 0.6f);
            }
            _pivot.localRotation = Quaternion.Euler(15, -35, 0);
        }
        public void OnDrag(PointerEventData e) { if (_pivot != null) _pivot.Rotate(Vector3.up, -e.delta.x * 0.5f, Space.World); }
        public void Clear()
        {
            if (_model != null) { _model.SetActive(false); Destroy(_model); _model = null; }
            if (_camera != null) _camera.gameObject.SetActive(false);
        }
        public void Release() { Clear(); if (_texture != null) { _camera.targetTexture = null; _image.texture = null; _texture.Release(); Destroy(_texture); _texture = null; } }
        private void OnDestroy() => Release();
    }
}
