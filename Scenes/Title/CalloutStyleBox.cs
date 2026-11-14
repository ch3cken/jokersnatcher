using Godot;
using System.Collections.Generic;

[Tool]
[GlobalClass]
public partial class CalloutStyleBox : StyleBox
{
	private Color _bgColor = new Color(0.16078432f, 0.18039216f, 0.23921569f, 0.74509805f);
	private Color _borderColor = new Color(0.45f, 0.5f, 0.62f, 1.0f);
	private float _borderWidth = 2.0f;
	private float _cornerRadius = 8.0f;
	private float _tailWidth = 22.0f;
	private float _tailHeight = 14.0f;
	private float _tailOffsetFromRight = 32.0f;

	public CalloutStyleBox()
	{
		UpdateMargins();
	}

	[Export]
	public Color BgColor
	{
		get => _bgColor;
		set { _bgColor = value; EmitChanged(); }
	}

	[Export]
	public Color BorderColor
	{
		get => _borderColor;
		set { _borderColor = value; EmitChanged(); }
	}

	[Export(PropertyHint.Range, "0,16,0.5")]
	public float BorderWidth
	{
		get => _borderWidth;
		set { _borderWidth = value; UpdateMargins(); EmitChanged(); }
	}

	[Export(PropertyHint.Range, "0,32,1")]
	public float CornerRadius
	{
		get => _cornerRadius;
		set { _cornerRadius = value; EmitChanged(); }
	}

	[Export(PropertyHint.Range, "4,64,1")]
	public float TailWidth
	{
		get => _tailWidth;
		set { _tailWidth = value; EmitChanged(); }
	}

	[Export(PropertyHint.Range, "0,48,1")]
	public float TailHeight
	{
		get => _tailHeight;
		set { _tailHeight = value; UpdateMargins(); EmitChanged(); }
	}

	[Export(PropertyHint.Range, "8,200,1")]
	public float TailOffsetFromRight
	{
		get => _tailOffsetFromRight;
		set { _tailOffsetFromRight = value; EmitChanged(); }
	}

	private void UpdateMargins()
	{
		ContentMarginLeft = _borderWidth;
		ContentMarginTop = _borderWidth;
		ContentMarginRight = _borderWidth;
		ContentMarginBottom = _borderWidth + _tailHeight;
	}

	public override void _Draw(Rid toCanvasItem, Rect2 rect)
	{
		float halfBorder = _borderWidth * 0.5f;
		float left = rect.Position.X + halfBorder;
		float top = rect.Position.Y + halfBorder;
		float right = rect.End.X - halfBorder;
		float boxBottom = rect.End.Y - _tailHeight - halfBorder;
		float tipY = rect.End.Y - halfBorder;

		float boxWidth = Mathf.Max(1.0f, right - left);
		float boxHeight = Mathf.Max(1.0f, boxBottom - top);
		float r = Mathf.Clamp(_cornerRadius, 0.0f, Mathf.Min(boxWidth, boxHeight) * 0.5f);

		List<Vector2> points = new List<Vector2>();

		// 1. Top-Left corner (180 deg to 270 deg)
		AddArcPoints(points, new Vector2(left + r, top + r), r, Mathf.Pi, Mathf.Pi * 1.5f);

		// 2. Top-Right corner (270 deg to 360 deg)
		AddArcPoints(points, new Vector2(right - r, top + r), r, Mathf.Pi * 1.5f, Mathf.Tau);

		// 3. Bottom-Right corner (0 deg to 90 deg)
		AddArcPoints(points, new Vector2(right - r, boxBottom - r), r, 0.0f, Mathf.Pi * 0.5f);

		// 4. Bottom triangle tail pointing down
		if (_tailHeight > 0.0f && _tailWidth > 0.0f)
		{
			float halfTail = _tailWidth * 0.5f;
			float minTipX = left + r + halfTail;
			float maxTipX = right - r - halfTail;
			float tipX = Mathf.Clamp(right - _tailOffsetFromRight, minTipX, Mathf.Max(minTipX, maxTipX));

			points.Add(new Vector2(tipX + halfTail, boxBottom));
			points.Add(new Vector2(tipX, tipY));
			points.Add(new Vector2(tipX - halfTail, boxBottom));
		}

		// 5. Bottom-Left corner (90 deg to 180 deg)
		AddArcPoints(points, new Vector2(left + r, boxBottom - r), r, Mathf.Pi * 0.5f, Mathf.Pi);

		Vector2[] polygonPoints = points.ToArray();

		// Draw unified filled callout shape (no seam between box and triangle even with transparency)
		RenderingServer.CanvasItemAddPolygon(
			toCanvasItem,
			polygonPoints,
			new Color[] { _bgColor }
		);

		// Draw continuous border around the entire shape including the triangle
		if (_borderWidth > 0.0f)
		{
			points.Add(points[0]);
			RenderingServer.CanvasItemAddPolyline(
				toCanvasItem,
				points.ToArray(),
				new Color[] { _borderColor },
				_borderWidth,
				true
			);
		}
	}

	private static void AddArcPoints(List<Vector2> points, Vector2 center, float radius, float startAngle, float endAngle, int segments = 8)
	{
		if (radius <= 0.01f)
		{
			points.Add(center);
			return;
		}

		for (int i = 0; i <= segments; i++)
		{
			float t = (float)i / segments;
			float angle = Mathf.Lerp(startAngle, endAngle, t);
			points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
		}
	}
}
