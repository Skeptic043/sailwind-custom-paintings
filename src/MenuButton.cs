using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomPaintings
{
    // A target on the picker page. The picker itself finds the button under the cursor and clicks
    // it; this draws the native hover outline around the button's own renderer.
    internal sealed class MenuButton : GoPointerButton
    {
        private static readonly FieldInfo OutlineField = AccessTools.Field(typeof(GoPointerButton), "outline");
        internal Action Clicked;
        // The button's area on the page, in page units.
        internal Func<Rect> Area;

        internal bool Hovered
        {
            set => overrideEnableOutline = value;
        }

        public override void Start()
        {
            // A button cloned from a native one that has already started carries its Outline.
            // GoPointerButton.UpdateColor reads the private field, so reuse that component.
            var existing = GetComponent("Outline");
            if (existing == null) base.Start();
            else OutlineField?.SetValue(this, existing);
        }

        public override void OnActivate()
        {
            if (!unclickable) Clicked?.Invoke();
        }
    }
}
