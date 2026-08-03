using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ADOFAI;

namespace Ponder
{
    public class PonderManager : MonoBehaviour
    {
        public static PonderManager Instance { get; private set; }

        private PonderHud hud;
        private PonderSceneView sceneView;
        private PonderSelector? hoveredSelector;
        private List<PonderSceneDef> _hoverScenes = new List<PonderSceneDef>();
        private PonderSubject? lastHovered;
        private bool charging;
        private float charge;
        private Exception lastError;
        private bool _enabled = true;

        public static void Ensure()
        {
            if (Instance != null)
            {
                return;
            }
            GameObject go = new GameObject("PonderManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PonderManager>();
        }

        public static void SetEnabled(bool enabled)
        {
            if (Instance == null)
            {
                return;
            }
            Instance._enabled = enabled;
            if (!enabled)
            {
                Instance.CloseAll();
            }
        }

        private void Update()
        {
            try
            {
                UpdateCore();
                lastError = null;
            }
            catch (Exception ex)
            {
                if (lastError == null || lastError.GetType() != ex.GetType() || lastError.Message != ex.Message)
                {
                    Main.Handler.Error("Ponder.Update: " + ex);
                }
                lastError = ex;
            }
        }

        private void UpdateCore()
        {
            if (!_enabled || !Main.Settings.enablePonder)
            {
                CloseAll();
                return;
            }

            scnEditor ed = ADOBase.editor;
            if (ed == null)
            {
                CloseAll();
                return;
            }

            PonderEngine.StartBackgroundScan();

            if (sceneView != null && sceneView.IsOpen)
            {
                sceneView.Tick();
                return;
            }

            if (hud == null)
            {
                hud = PonderHud.Create(GetFont());
            }
            if (sceneView == null)
            {
                sceneView = PonderSceneView.Create(GetFont());
            }

            if (!Main.Settings.enableHoverHud)
            {
                StopCharging();
                hud.SetCharging(false, 0f);
                hud.Hide();
                return;
            }

            var hover = FindHoveredTarget(ed);
            var scenes = hover != null ? PonderEngine.FindScenes(hover) : new List<PonderSceneDef>();
            PonderSceneDef? scene = scenes.Count > 0 ? scenes[0] : null;
            _hoverScenes = scenes;

            if (hover != null && hoveredSelector != null && hover.IdentityEquals(hoveredSelector))
            {
                // unchanged
            }
            else
            {
                hoveredSelector = hover;
                if (Main.Settings.enableDebugLogs)
                {
                    Main.Handler.Log(scene != null
                        ? $"Ponder: hover → {scene.Title} ({hover?.Describe()})"
                        : "Ponder: hover → none");
                }
            }

            if (scene == null)
            {
                StopCharging();
                hud.SetCharging(false, 0f);
                hud.Hide();
                return;
            }

            var subject = new PonderSubject(scene.Title, scene.Description);
            if (lastHovered == null || lastHovered.Title != subject.Title)
            {
                lastHovered = subject;
            }
            hud.SetSubject(subject);
            hud.ShowAt(Input.mousePosition);

            if (ed.userIsEditingAnInputField)
            {
                StopCharging();
                hud.SetCharging(false, 0f);
                return;
            }

            bool altDown = IsAltDown();
            bool altUp = IsAltUp();

            if (!charging)
            {
                if (altDown)
                {
                    charging = true;
                    charge = 0f;
                    hud.SetCharging(true, 0f);
                }
            }
            else
            {
                if (altUp)
                {
                    StopCharging();
                    hud.SetCharging(false, 0f);
                }
                else if (altDown)
                {
                    float chargeTime = Main.Settings.chargeTime > 0.05f ? Main.Settings.chargeTime : 2f;
                    charge += Time.deltaTime / chargeTime;
                    hud.SetCharging(true, Mathf.Clamp01(charge));
                    if (charge >= 1f)
                    {
                        StopCharging();
                        hud.SetCharging(false, 0f);
                        hud.Hide();
                        sceneView.Show(_hoverScenes.Count > 0 ? _hoverScenes : new List<PonderSceneDef> { scene });
                    }
                }
            }
        }

        private static bool IsAltDown()
        {
            return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        }

        private static bool IsAltUp()
        {
            return Input.GetKeyUp(KeyCode.LeftAlt) || Input.GetKeyUp(KeyCode.RightAlt);
        }

        /// <summary>
        /// 检测鼠标当前悬停的目标，并描述成通用身份选择器：
        /// 编辑器 Inspector 面板（事件/设置标题）、装饰物、砖块、以及任意可拾取 GameObject。
        /// </summary>
        private PonderSelector? FindHoveredTarget(scnEditor ed)
        {
            var fromPanel = HoveredInspectorPanel(ed);
            if (fromPanel != null)
            {
                return fromPanel;
            }
            var fromObjects = HoveredWorldObject();
            if (fromObjects != null)
            {
                return fromObjects;
            }
            return null;
        }

        private static PonderSelector? HoveredInspectorPanel(scnEditor ed)
        {
            InspectorPanel[] panels = { ed.levelEventsPanel, ed.settingsPanel };
            foreach (InspectorPanel panel in panels)
            {
                if (panel == null || panel.title == null)
                {
                    continue;
                }
                if (panel.selectedEventType == LevelEventType.None)
                {
                    continue;
                }
                if (!panel.title.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Camera cam = null;
                Canvas canvas = panel.title.canvas;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    cam = canvas.worldCamera;
                }

                if (!RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)panel.title.transform, Input.mousePosition, cam))
                {
                    continue;
                }

                var et = panel.selectedEventType.ToString();
                var sel = new PonderSelector { Component = "InspectorPanel" };
                if (panel.selectedEventType.IsSetting())
                {
                    sel.Setting = et;
                }
                else
                {
                    sel.Event = et;
                }
                return sel;
            }
            return null;
        }

        /// <summary>通用世界悬停：把鼠标下的 GameObject 描述成身份选择器（名称/tag/组件/层级/砖块）。</summary>
        private static PonderSelector? HoveredWorldObject()
        {
            var cam = ADOBase.controller != null && ADOBase.controller.camy != null
                ? ADOBase.controller.camy.camobj
                : Camera.main;
            if (cam == null)
            {
                return null;
            }
            var world = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition);

            // 装饰物（用自己的 hitbox collider）
            var dmg = scrDecorationManager.instance;
            if (dmg != null && dmg.allDecorations != null)
            {
                foreach (var d in dmg.allDecorations)
                {
                    if (d == null)
                    {
                        continue;
                    }
                    if (d.activeCollider != null && d.activeCollider.OverlapPoint(world))
                    {
                        return new PonderSelector
                        {
                            Component = "scrDecoration",
                            Tag = d.decorationTag ?? "",
                            Floor = false
                        };
                    }
                }
            }

            // 通用物理拾取：Floor 层（砖块）以及其他可碰撞层
            var hits = Physics2D.OverlapPointAll(world);
            if (hits.Length > 0)
            {
                Array.Sort(hits, static (a, b) => CompareSorting(a, b));
                var go = hits[0].gameObject;
                var layerName = LayerMask.LayerToName(go.layer);
                var sel = new PonderSelector
                {
                    Name = go.name,
                    Tag = go.tag,
                    Component = go.GetComponent<scrFloor>() != null ? "scrFloor" : "",
                    Layer = layerName,
                    Floor = go.GetComponent<scrFloor>() != null
                };
                return sel;
            }
            return null;
        }

        private static int CompareSorting(Collider2D a, Collider2D b)
        {
            var ra = a.GetComponentInParent<Renderer>();
            var rb = b.GetComponentInParent<Renderer>();
            var sa = ra != null ? ra.sortingOrder : int.MinValue;
            var sb = rb != null ? rb.sortingOrder : int.MinValue;
            return sb.CompareTo(sa);
        }

        private static TMP_FontAsset GetFont()
        {
            if (RDString.editorFonts != null && RDString.editorFonts.Length > 0 && RDString.editorFonts[0] != null)
            {
                return RDString.editorFonts[0];
            }
            return TMP_Settings.defaultFontAsset;
        }

        private void StopCharging()
        {
            charging = false;
            charge = 0f;
        }

        private void CloseAll()
        {
            StopCharging();
            if (sceneView != null && sceneView.IsOpen)
            {
                sceneView.Close();
            }
            if (hud != null)
            {
                hud.SetCharging(false, 0f);
                hud.Hide();
            }
        }
    }
}
