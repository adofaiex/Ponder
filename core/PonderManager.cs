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
        private PonderSubject? lastHovered;
        private bool charging;
        private float charge;
        private Exception lastError;

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
            if (!Main.Settings.enablePonder)
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

            if (hover != null && hoveredSelector != null &&
                hover.Type == hoveredSelector.Type &&
                hover.Event == hoveredSelector.Event &&
                hover.Setting == hoveredSelector.Setting &&
                hover.Tag == hoveredSelector.Tag &&
                hover.Image == hoveredSelector.Image &&
                hover.Floor == hoveredSelector.Floor)
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
                        sceneView.Show(scene);
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

        /// <summary>检测鼠标当前悬停的目标（事件标题 / 设置标题 / 装饰物 / 砖块）。</summary>
        private PonderSelector? FindHoveredTarget(scnEditor ed)
        {
            var fromPanel = HoveredInspectorPanel(ed);
            if (fromPanel != null)
            {
                return fromPanel;
            }
            var fromDeco = HoveredDecoration();
            if (fromDeco != null)
            {
                return fromDeco;
            }
            if (HoveredFloor())
            {
                return new PonderSelector { Type = "floor", Floor = true };
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
                if (panel.selectedEventType.IsSetting())
                {
                    return new PonderSelector { Type = "setting", Setting = et };
                }
                return new PonderSelector { Type = "event", Event = et };
            }
            return null;
        }

        private static PonderSelector? HoveredDecoration()
        {
            var dmg = scrDecorationManager.instance;
            if (dmg == null || dmg.allDecorations == null || dmg.allDecorations.Count == 0)
            {
                return null;
            }
            var cam = ADOBase.controller != null && ADOBase.controller.camy != null
                ? ADOBase.controller.camy.camobj
                : Camera.main;
            if (cam == null)
            {
                return null;
            }
            var world = cam.ScreenToWorldPoint(Input.mousePosition);
            foreach (var d in dmg.allDecorations)
            {
                if (d == null)
                {
                    continue;
                }
                if (d.activeCollider != null && d.activeCollider.OverlapPoint(world))
                {
                    return new PonderSelector { Type = "decoration", Tag = d.decorationTag ?? "" };
                }
            }
            return null;
        }

        private static bool HoveredFloor()
        {
            var cam = ADOBase.controller != null && ADOBase.controller.camy != null
                ? ADOBase.controller.camy.camobj
                : Camera.main;
            if (cam == null)
            {
                return false;
            }
            var world = cam.ScreenToWorldPoint(Input.mousePosition);
            var mask = 1 << LayerMask.NameToLayer("Floor");
            return Physics2D.OverlapPoint(world, mask) != null;
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
