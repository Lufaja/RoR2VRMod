using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRMod.Inputs
{
    /// <summary>
    /// Small panel on the left wrist while the combo is armed: key item name, ready state, and the
    /// buttons. After an action it briefly shows what happened ("Used ...", "Element ►").
    /// </summary>
    internal static class KeyItemIndicator
    {
        private static GameObject panel;
        private static TextMeshProUGUI text;
        private static Canvas hostCanvas;
        private static float flashUntil;
        private static string flashText;
        private static bool holdVisible;
        private static string statusText;

        /// <summary>Persistent message on the wrist panel (e.g. mapping freeze); null clears it.</summary>
        internal static void SetStatus(string text)
        {
            statusText = text;
            Refresh(null);
        }

        internal static void Flash(KeyItemSlot slot, string what)
        {
            string name = ItemName(slot);
            flashText = what == "Used" ? $"<b>Used {name}</b>" : $"<b>{name}</b>\n{what}";
            flashUntil = Time.unscaledTime + 1.2f;
            Refresh(slot);
        }

        internal static void Show(bool holding, KeyItemSlot slot)
        {
            holdVisible = holding;
            Refresh(slot);
        }

        private static void Refresh(KeyItemSlot slot)
        {
            bool flashing = Time.unscaledTime < flashUntil;
            bool visible = holdVisible || flashing || statusText != null;
            if (!visible)
            {
                if (panel && panel.activeSelf) panel.SetActive(false);
                return;
            }
            if (!Ensure()) return;
            if (!panel.activeSelf) panel.SetActive(true);

            if (statusText != null && !holdVisible && !flashing)
            {
                text.text = statusText;
                return;
            }

            if (flashing && !holdVisible)
            {
                text.text = flashText;
                return;
            }

            string state = "";
            try
            {
                if (slot)
                {
                    if (slot.isDisabled) state = "  <color=#ff8080>disabled</color>";
                    else if (slot.bodyBehavior && !slot.bodyBehavior.IsReadyToExecute()) state = "  <color=#ffd070>recharging</color>";
                    else state = "  <color=#90ff90>ready</color>";
                }
            }
            catch { }

            string use = ViveControllers.IsTrackpadWand ? "Pad ▼" : "A";
            string cycle = ViveControllers.IsTrackpadWand ? "Pad ◄ ►" : "Stick ◄ ►";
            string header = flashing ? flashText : $"<b>{ItemName(slot)}</b>{state}";
            text.text = $"{header}\n<size=80%>{use}: use    {cycle}: element</size>";
        }

        private static string ItemName(KeyItemSlot slot)
        {
            try
            {
                if (slot)
                {
                    KeyItemDef def = KeyItemCatalog.GetKeyItemDef(slot.keyItem);
                    if (def) return Language.GetString(def.nameToken);
                }
            }
            catch { }
            return "Key item";
        }

        private static bool Ensure()
        {
            if (panel && hostCanvas) return true;
            if (!MotionControls.HandsReady) return false;
            HandController leftHand = MotionControls.GetHandBySide(true);
            Canvas canvas = leftHand && leftHand.smallHud ? leftHand.smallHud.Canvas : null;
            if (!canvas) return false;

            if (panel) Object.Destroy(panel);
            hostCanvas = canvas;

            panel = new GameObject("VRKeyItemIndicator", typeof(RectTransform));
            panel.layer = canvas.gameObject.layer;
            RectTransform rt = panel.transform as RectTransform;
            rt.SetParent(canvas.transform, false);
            rt.sizeDelta = new Vector2(520, 130);
            rt.localPosition = new Vector3(0, 260, 0);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;

            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.85f);
            bg.raycastTarget = false;

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.layer = panel.layer;
            RectTransform trt = textObject.transform as RectTransform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16, 8); trt.offsetMax = new Vector2(-16, -8);
            text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = 30;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.92f, 0.75f, 1f);
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            try
            {
                TMP_Text donor = RoR2Application.instance && RoR2Application.instance.mainCanvas
                    ? RoR2Application.instance.mainCanvas.GetComponentInChildren<TMP_Text>(true) : null;
                if (donor && donor.font) { text.font = donor.font; if (donor.fontSharedMaterial) text.fontSharedMaterial = donor.fontSharedMaterial; }
            }
            catch { }

            panel.SetActive(false);
            VRMod.StaticLogger.LogInfo("[VR key item] wrist indicator created.");
            return true;
        }
    }
}
