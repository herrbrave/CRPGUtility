using UnityEngine.UIElements;

namespace VikingRaid.Tests
{
    /// <summary>How the PlayMode tests press a UI Toolkit button headlessly (the technique BarrowsEnd proved): a NavigationSubmit fires <c>clicked</c> with no hit-test. <c>target</c> MUST be set.</summary>
    internal static class UiClick
    {
        public static void Submit(VisualElement element)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = element;
            element.SendEvent(submit);
        }
    }
}
