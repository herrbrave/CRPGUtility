using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Tests
{
    /// <summary>
    /// How the PlayMode tests click a UI Toolkit control (Task 4.2 spike; findings in Docs/Plans/2D-Campaign-Phase4-CharacterCreator.md).
    /// <see cref="Pointer"/> sends a real pointer press/release at the element's centre through the panel's own dispatch, so it exercises
    /// <c>Clickable</c>'s hit-test; <see cref="Submit"/> sends a NavigationSubmit that fires <c>clicked</c> with no hit-test. The pointer-event
    /// position/pointer-id setters are protected, so the only public route is the <c>GetPooled(Event)</c> overload with <c>button = 0</c>.
    /// A live panel needs two frames after building before <c>worldBound</c> is valid.
    /// </summary>
    internal static class UiClick
    {
        public static void Pointer(VisualElement element)
        {
            var p = element.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = p, button = 0, clickCount = 1 }))
            {
                down.target = element;
                element.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = p, button = 0, clickCount = 1 }))
            {
                up.target = element;
                element.SendEvent(up);
            }
        }

        /// <summary>The guaranteed floor: <c>Button</c> answers a NavigationSubmit by invoking <c>clicked</c>. <c>target</c> MUST be set, else the
        /// event goes to the focused element or the panel root.</summary>
        public static void Submit(VisualElement element)
        {
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = element;
                element.SendEvent(submit);
            }
        }
    }
}
