using System.Collections.Generic;
using CRPG.Camera;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// 2D campaign Task 3.1: validates an orthographic pixel-art <see cref="CRPGCameraSettings"/> -- pure logic, no scene. Settings that are not
    /// orthographic are never checked (the perspective rig is unchanged). What it guards: an empty or unordered zoom list (the rig would have no
    /// scale to step through), a default scale that is not in the list, and the two things sprite sorting quietly depends on: yaw locked at 0
    /// (rotation speed and snap both 0) and pitch 90 (the flat sprite cards face straight up the camera axis). Never mutates the asset.
    /// </summary>
    public static class CameraSettingsValidator
    {
        public static List<ValidationResult> Validate(CRPGCameraSettings settings, string assetPath)
        {
            var results = new List<ValidationResult>();
            if (settings == null || !settings.Orthographic) return results;

            var scales = settings.ZoomScales;
            if (scales == null || scales.Count == 0)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "zoomScales",
                    "Orthographic mode needs at least one zoom scale.", "Add scales such as 2, 3, 4, 5, 6."));
            }
            else
            {
                for (var i = 0; i < scales.Count; i++)
                {
                    if (scales[i] < 1)
                        results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"zoomScales[{i}]",
                            $"Scale {scales[i]} is not a positive integer.", "Scales are screen pixels per source pixel: 1 or more."));
                    else if (i > 0 && scales[i] <= scales[i - 1])
                        results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, $"zoomScales[{i}]",
                            $"Scale {scales[i]} is not greater than the one before it.", "Keep the list strictly ascending: zoom steps walk it in order."));
                }

                if (!Contains(scales, settings.DefaultScale))
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "defaultScale",
                        $"Default scale {settings.DefaultScale} is not in the zoom list; the nearest listed scale is used.", "Pick a listed scale."));
            }

            if (settings.RotationSpeed != 0f || settings.SnapRotationDegrees != 0f)
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "rotationSpeed",
                    "Yaw is not locked: a top-down sprite camera must stay at yaw 0 or depth sorting and the billboard cards break.",
                    "Set Rotation Speed and Snap Rotation Degrees to 0."));

            if (settings.MinimumPitch != 90f || settings.MaximumPitch != 90f || settings.DefaultPitch != 90f)
                results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, "pitch",
                    "Pitch is not 90 degrees: the flat sprite cards assume the camera looks straight down.",
                    "Set Minimum, Maximum and Default Pitch to 90."));

            return results;
        }

        private static bool Contains(IReadOnlyList<int> scales, int value)
        {
            for (var i = 0; i < scales.Count; i++)
                if (scales[i] == value) return true;
            return false;
        }
    }
}
