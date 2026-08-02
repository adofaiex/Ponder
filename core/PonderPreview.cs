using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ponder
{
    public sealed class PonderPreview : IDisposable
    {
        public const int PreviewLayer = 30;

        public RenderTexture? Texture { get; private set; }
        public float WorldSize { get; private set; }
        public bool IsReady { get; private set; }
        public int TileCount => _tileBases.Count;

        private GameObject? _root;
        private GameObject? _cameraGo;
        private Camera? _camera;
        private Bounds _bounds;

        private readonly List<scrFloor?> _tileFloors = new List<scrFloor?>();
        private readonly List<Vector3> _tileBases = new List<Vector3>();
        private readonly List<Vector3> _tileExtra = new List<Vector3>();
        private readonly List<float> _tileScale = new List<float>();

        private Vector3 _globalDelta;
        private float _globalScale = 1f;

        private readonly List<GameObject> _objects = new List<GameObject>();

        private sealed class DecoObj
        {
            public GameObject? go;
            public Vector3 basePos;
            public Vector3 extra;
            public float baseScale = 1f;
            public int tile;
            public string tag = "";
            public SpriteRenderer? sr;
            public bool spawned;
        }
        private readonly List<DecoObj> _decos = new List<DecoObj>();

        public Vector3 GlobalDelta => _globalDelta;
        public float GlobalScale => _globalScale;

        public void ResetTransforms()
        {
            _globalDelta = Vector3.zero;
            _globalScale = 1f;
            for (var i = 0; i < _tileExtra.Count; i++)
            {
                _tileExtra[i] = Vector3.zero;
                _tileScale[i] = 1f;
            }
            ResetTileColors();
            foreach (var deco in _decos)
            {
                deco.extra = Vector3.zero;
                if (deco.sr != null)
                {
                    var c = deco.sr.color;
                    c.a = deco.spawned ? 0f : 1f;
                    deco.sr.color = c;
                }
            }
            ApplyTransforms();
        }

        public bool Build(PonderSceneDef scene)
        {
            Clear();
            try
            {
                if (scrLevelMaker.instance == null || scrLevelMaker.instance.spriteFloor == null)
                {
                    Main.Handler?.Error("PonderPreview: no scrLevelMaker.spriteFloor");
                    return false;
                }

                _root = new GameObject("PonderPreviewRoot");
                UnityEngine.Object.DontDestroyOnLoad(_root);

                var tileSize = GetTileSize();
                var pos = Vector3.zero;
                var floorPositions = new List<Vector3>();
                var prevExit = 4.71238898038469;
                scrFloor? lastFloor = null;
                for (var i = 0; i < scene.Tiles.Count; i++)
                {
                    var tile = scene.Tiles[i];
                    var floorGo = UnityEngine.Object.Instantiate(scrLevelMaker.instance.spriteFloor, _root.transform);
                    _objects.Add(floorGo);
                    SetLayer(floorGo, PreviewLayer);
                    var floor = floorGo.GetComponent<scrFloor>();
                    if (floor != null)
                    {
                        floor.styleNum = i == 0 ? -1 : tile.Style;
                        var exit = (-(double)tile.Angle + 90.0) * Math.PI / 180.0;
                        floor.entryangle = i == 0 ? 4.71238898038469 : Mod2Pi(prevExit + Math.PI);
                        floor.exitangle = exit;
                        floor.midSpin = tile.Midspin;
                        floor.UpdateAngle(true);
                        prevExit = exit;
                        lastFloor = floor;
                    }
                    floorGo.transform.position = pos;
                    pos += tileSize * Dir(prevExit);
                    floorPositions.Add(floorGo.transform.position);
                    _tileFloors.Add(floor);
                    _tileBases.Add(floorGo.transform.position);
                    _tileExtra.Add(Vector3.zero);
                    _tileScale.Add(1f);
                }
                if (lastFloor != null)
                {
                    lastFloor.exitangle = lastFloor.entryangle + Math.PI;
                }

                if (scene.Decos.Count > 0 && scrDecorationManager.instance != null)
                {
                    foreach (var deco in scene.Decos)
                    {
                        CreateDeco(scene, deco, floorPositions, tileSize);
                    }
                }

                if (floorPositions.Count == 0)
                {
                    floorPositions.Add(Vector3.zero);
                }
                var bounds = ComputeBounds(floorPositions, tileSize);
                WorldSize = bounds.size.magnitude;
                SetupCamera(scene, bounds);
                IsReady = true;
                return true;
            }
            catch (Exception ex)
            {
                Main.Handler?.Error($"PonderPreview.Build failed\n{ex}");
                Clear();
                return false;
            }
        }

        private void CreateDeco(PonderSceneDef scene, PonderSceneDeco deco, List<Vector3> floorPositions, float tileSize)
        {
            var sprite = PonderEngine.GetImage(scene, deco.Image);
            if (sprite == null)
            {
                return;
            }
            var pos = floorPositions[Mathf.Clamp(deco.Tile, 0, floorPositions.Count - 1)];
            pos += new Vector3(deco.OffsetX, deco.OffsetY, 0f) * tileSize;
            var go = new GameObject($"Deco_{deco.Image}");
            go.transform.SetParent(_root!.transform, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, 0, deco.Rotation);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = deco.Depth * 10;
            sr.color = Color.white;
            SetLayer(go, PreviewLayer);
            _objects.Add(go);

            _decos.Add(new DecoObj
            {
                go = go,
                basePos = pos,
                baseScale = deco.Scale,
                tile = Mathf.Clamp(deco.Tile, 0, Mathf.Max(0, floorPositions.Count - 1)),
                tag = deco.Tag ?? "",
                sr = sr
            });
        }

        // ================= 命令驱动接口 =================

        public void SetGlobalDelta(Vector3 delta)
        {
            _globalDelta = delta;
        }

        public void SetGlobalScale(float scale)
        {
            _globalScale = scale;
        }

        public void SetTileExtraDelta(int from, int to, Vector3 delta)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileExtra.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileExtra.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileExtra[i] = delta;
            }
        }

        public void SetTileScale(int from, int to, float scale)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileScale.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileScale.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileScale[i] = scale;
            }
        }

        public void SetTileColorRange(int from, int to, Color color)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileFloors.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileFloors.Count - 1));
            for (var i = from; i <= to; i++)
            {
                _tileFloors[i]?.SetTileColor(color);
            }
        }

        public void ResetTileColors()
        {
            foreach (var floor in _tileFloors)
            {
                floor?.SetTileColor(Color.white);
            }
        }

        public void SetTileStyleRange(int from, int to, int style)
        {
            from = Mathf.Clamp(from, 0, Mathf.Max(0, _tileFloors.Count - 1));
            to = Mathf.Clamp(to, from, Mathf.Max(from, _tileFloors.Count - 1));
            for (var i = from; i <= to; i++)
            {
                var floor = _tileFloors[i];
                if (floor == null)
                {
                    continue;
                }
                floor.styleNum = style;
                floor.UpdateAngle(true);
            }
        }

        public int AddDeco(PonderSceneDef scene, string image, int tile, float offsetX, float offsetY, float scale, float rotation, int depth, string tag)
        {
            var sprite = PonderEngine.GetImage(scene, image);
            if (sprite == null || _tileBases.Count == 0)
            {
                return -1;
            }
            tile = Mathf.Clamp(tile, 0, _tileBases.Count - 1);
            var tileSize = GetTileSize();
            var basePos = _tileBases[tile] + new Vector3(offsetX, offsetY, 0f) * tileSize;
            var go = new GameObject($"DecoCmd_{image}");
            go.transform.SetParent(_root!.transform, false);
            go.transform.position = basePos;
            go.transform.rotation = Quaternion.Euler(0, 0, rotation);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = depth * 10;
            sr.color = new Color(1f, 1f, 1f, 0f);
            SetLayer(go, PreviewLayer);
            _objects.Add(go);
            _decos.Add(new DecoObj
            {
                go = go,
                basePos = basePos,
                baseScale = scale,
                tile = tile,
                tag = tag ?? "",
                sr = sr,
                spawned = true
            });
            return _decos.Count - 1;
        }

        public void SetDecoAlpha(int index, float alpha)
        {
            if (index < 0 || index >= _decos.Count)
            {
                return;
            }
            var sr = _decos[index].sr;
            if (sr != null)
            {
                var c = sr.color;
                c.a = Mathf.Clamp01(alpha);
                sr.color = c;
            }
        }

        public void SetDecoTaggedDelta(string tag, Vector3 delta)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }
            foreach (var deco in _decos)
            {
                if (deco.tag == tag)
                {
                    deco.extra = delta;
                }
            }
        }

        public void SetDecoTaggedRotation(string tag, float rotation)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }
            foreach (var deco in _decos)
            {
                if (deco.tag == tag && deco.go != null)
                {
                    deco.go.transform.rotation = Quaternion.Euler(0, 0, rotation);
                }
            }
        }

        public void ApplyTransforms()
        {
            for (var i = 0; i < _tileBases.Count; i++)
            {
                if (_root == null)
                {
                    return;
                }
                var go = _objects[i];
                if (go == null)
                {
                    continue;
                }
                go.transform.position = _tileBases[i] + _globalDelta + _tileExtra[i];
                var s = _globalScale * _tileScale[i];
                go.transform.localScale = new Vector3(s, s, 1f);
            }
            foreach (var deco in _decos)
            {
                if (deco.go == null)
                {
                    continue;
                }
                var extra = _globalDelta + deco.extra;
                if (deco.tile >= 0 && deco.tile < _tileExtra.Count)
                {
                    extra += _tileExtra[deco.tile];
                }
                deco.go.transform.position = deco.basePos + extra;
                var ds = _globalScale * deco.baseScale;
                deco.go.transform.localScale = new Vector3(ds, ds, 1f);
            }
        }

        // ================= 相机 =================

        private static float GetTileSize()
        {
            try
            {
                if (ADOBase.controller != null)
                {
                    return ADOBase.controller.tileSize;
                }
            }
            catch
            {
            }
            return 1f;
        }

        private static double Mod2Pi(double angle)
        {
            angle %= Math.PI * 2.0;
            if (angle < 0.0)
            {
                angle += Math.PI * 2.0;
            }
            return angle;
        }

        private static Vector3 Dir(double exitAngle)
        {
            return new Vector3(Mathf.Sin((float)exitAngle), Mathf.Cos((float)exitAngle), 0f);
        }

        private static Bounds ComputeBounds(List<Vector3> floorPositions, float tileSize)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            var max = new Vector3(float.MinValue, float.MinValue, 0f);
            foreach (var p in floorPositions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var margin = tileSize * 2f;
            min -= new Vector3(margin, margin, 0f);
            max += new Vector3(margin, margin, 0f);
            var size = max - min;
            var center = (min + max) * 0.5f;
            return new Bounds(center, size);
        }

        private void SetupCamera(PonderSceneDef scene, Bounds bounds)
        {
            _bounds = bounds;
            Texture = new RenderTexture(1280, 720, 16);
            Texture.name = "PonderPreviewRT";

            _cameraGo = new GameObject("PonderPreviewCamera");
            _cameraGo.transform.SetParent(_root!.transform, false);
            _cameraGo.layer = PreviewLayer;
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.020f, 0.028f, 0.042f, 1f);
            _camera.orthographic = true;
            _camera.orthographicSize = scene.Zoom;
            _camera.targetTexture = Texture;
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.nearClipPlane = -100f;
            _camera.farClipPlane = 100f;
            _cameraGo.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
        }

        public void FitTrack()
        {
            if (_camera == null || Texture == null)
            {
                return;
            }
            var aspect = (float)Texture.width / Texture.height;
            var size = _bounds.size;
            var ortho = Mathf.Max(size.y * 0.5f, size.x / (2f * aspect)) * 1.15f;
            _camera.orthographicSize = Mathf.Max(ortho, 1.5f);
            var center = _bounds.center;
            _cameraGo!.transform.position = new Vector3(center.x, center.y, -10f);
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayer(child.gameObject, layer);
            }
        }

        public void FocusFloor(int floorIndex)
        {
            if (_cameraGo == null || _tileBases.Count == 0)
            {
                return;
            }
            var p = _tileBases[Mathf.Clamp(floorIndex, 0, _tileBases.Count - 1)];
            if (floorIndex >= 0 && floorIndex < _tileExtra.Count)
            {
                p += _globalDelta + _tileExtra[floorIndex];
            }
            p.z = -10f;
            _cameraGo.transform.position = p;
        }

        public void Clear()
        {
            IsReady = false;
            foreach (var go in _objects)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            _objects.Clear();
            _decos.Clear();
            _tileFloors.Clear();
            _tileBases.Clear();
            _tileExtra.Clear();
            _tileScale.Clear();
            _globalDelta = Vector3.zero;
            _globalScale = 1f;
            if (_cameraGo != null)
            {
                UnityEngine.Object.Destroy(_cameraGo);
                _cameraGo = null;
                _camera = null;
            }
            if (Texture != null)
            {
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
