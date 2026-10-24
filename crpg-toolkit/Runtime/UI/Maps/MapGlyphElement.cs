using System.Collections.Generic;
using CRPG.Maps;
using UnityEngine;
using UnityEngine.UIElements;

namespace CRPG.UI
{
    /// <summary>
    /// A marker box that can draw a <see cref="MapGlyphShape"/> as a filled, outlined polygon (UI Toolkit styles can only make boxes, circles and a rotated box). Used for landmarks without an icon and for pins; the
    /// plain diamond keeps its styled-box form so existing looks do not change. A pin carries its number as a centred label. Configured by <see cref="MapMarkerGlyphs.Apply"/>.
    /// </summary>
    public sealed class MapGlyphElement : VisualElement
    {
        private MapGlyphShape shape; private Color fill = Color.white, outline = Color.black; private bool drawn; private Label numberLabel;

        public MapGlyphElement()
        {
            pickingMode = PickingMode.Position;
            generateVisualContent += Draw;
        }

        public MapGlyphShape Shape => shape;
        public bool IsDrawn => drawn;
        public Label NumberLabel => numberLabel;

        /// <summary>Draws <paramref name="newShape"/> in <paramref name="newFill"/> with an outline; a pin shows <paramref name="number"/>.</summary>
        public void Show(MapGlyphShape newShape, Color newFill, Color newOutline, int number)
        {
            shape = newShape; fill = newFill; outline = newOutline; drawn = true;
            if (number > 0)
            {
                if (numberLabel == null)
                {
                    numberLabel = new Label { name = "map-pin-number", pickingMode = PickingMode.Ignore };
                    numberLabel.style.position = Position.Absolute; numberLabel.style.left = 0; numberLabel.style.right = 0; numberLabel.style.top = 0;
                    numberLabel.style.unityTextAlign = TextAnchor.MiddleCenter; numberLabel.style.fontSize = 10; numberLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    numberLabel.style.color = Color.white; Add(numberLabel);
                }

                numberLabel.text = number.ToString(); numberLabel.style.display = DisplayStyle.Flex;
                numberLabel.style.height = resolvedStyle.width > 0f ? resolvedStyle.width * 0.8f : style.height.value.value * 0.8f;
            }
            else if (numberLabel != null) numberLabel.style.display = DisplayStyle.None;
            MarkDirtyRepaint();
        }

        /// <summary>Back to a plain styled box (the diamond or an icon): nothing is drawn here.</summary>
        public void Clear()
        {
            if (!drawn) return;
            drawn = false;
            if (numberLabel != null) numberLabel.style.display = DisplayStyle.None;
            MarkDirtyRepaint();
        }

        /// <summary>The outline of a shape in a unit box (0..1, y down), the vertices in order. Pure, so the silhouettes are testable.</summary>
        public static IReadOnlyList<Vector2> Outline(MapGlyphShape shape)
        {
            var points = new List<Vector2>();
            switch (shape)
            {
                case MapGlyphShape.Square: points.AddRange(new[] { new Vector2(.1f, .1f), new Vector2(.9f, .1f), new Vector2(.9f, .9f), new Vector2(.1f, .9f) }); break;
                case MapGlyphShape.Triangle: points.AddRange(new[] { new Vector2(.5f, .06f), new Vector2(.95f, .88f), new Vector2(.05f, .88f) }); break;
                case MapGlyphShape.Hexagon: for (var i = 0; i < 6; i++) points.Add(OnCircle(i * 60f, .46f)); break;
                case MapGlyphShape.Star: for (var i = 0; i < 10; i++) points.Add(OnCircle(-90f + i * 36f, i % 2 == 0 ? .5f : .22f)); break;
                case MapGlyphShape.Cross:
                    points.AddRange(new[]
                    {
                        new Vector2(.36f, .05f), new Vector2(.64f, .05f), new Vector2(.64f, .36f), new Vector2(.95f, .36f), new Vector2(.95f, .64f), new Vector2(.64f, .64f),
                        new Vector2(.64f, .95f), new Vector2(.36f, .95f), new Vector2(.36f, .64f), new Vector2(.05f, .64f), new Vector2(.05f, .36f), new Vector2(.36f, .36f),
                    });
                    break;
                case MapGlyphShape.Pin:
                    points.Add(new Vector2(.5f, .98f));
                    for (var a = 130f; a <= 410f; a += 20f) points.Add(new Vector2(.5f, .38f) + new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * .34f);
                    break;
                case MapGlyphShape.Circle:
                case MapGlyphShape.Ring: for (var i = 0; i < 24; i++) points.Add(OnCircle(i * 15f, .44f)); break;
                default: points.AddRange(new[] { new Vector2(.5f, .02f), new Vector2(.98f, .5f), new Vector2(.5f, .98f), new Vector2(.02f, .5f) }); break;   // Diamond
            }

            return points;
        }

        private static Vector2 OnCircle(float degrees, float radius) => new Vector2(.5f, .5f) + new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

        private void Draw(MeshGenerationContext context)
        {
            if (!drawn) return;
            var box = contentRect;
            if (box.width <= 0f || box.height <= 0f) return;
            var painter = context.painter2D;
            var points = Outline(shape);
            painter.lineWidth = 1.5f; painter.strokeColor = outline; painter.fillColor = fill;
            painter.BeginPath();
            for (var i = 0; i < points.Count; i++)
            {
                var p = new Vector2(box.x + points[i].x * box.width, box.y + points[i].y * box.height);
                if (i == 0) painter.MoveTo(p); else painter.LineTo(p);
            }

            painter.ClosePath();
            if (shape == MapGlyphShape.Ring)
            {
                painter.Stroke();   // a ring is the outline with the middle left open
                painter.lineWidth = 3f; painter.strokeColor = fill; painter.Stroke();
                return;
            }

            painter.Fill(); painter.Stroke();
        }
    }
}
