using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CustomPaintings
{
    // A pointer target on the picker page. The game's menu pointer calls OnActivate on click and
    // draws the native hover outline around this object's renderer.
    internal sealed class MenuButton : GoPointerButton
    {
        private static readonly FieldInfo OutlineField = AccessTools.Field(typeof(GoPointerButton), "outline");
        internal Action Clicked;

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
