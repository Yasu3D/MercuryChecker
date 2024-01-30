using SkiaSharp;
using System;

namespace MercuryMapChecker;

public class SkiaRenderEngine
{
    private static readonly SKColor BackgroundColor = new(0xEC, 0xEC, 0xEB, 0xFF);
    private static readonly SKColor OutlineColor = new(0x17, 0x17, 0x17, 0xFF);

    private static readonly SKColor weight0  = new(0xF8, 0xE6, 0x22, 0xFF);
    private static readonly SKColor weight1  = new(0xC5, 0xDE, 0x32, 0xFF);
    private static readonly SKColor weight2  = new(0x93, 0xD7, 0x42, 0xFF);
    private static readonly SKColor weight3  = new(0x6E, 0xCC, 0x57, 0xFF);
    private static readonly SKColor weight4  = new(0x49, 0xC1, 0x6D, 0xFF);
    private static readonly SKColor weight5  = new(0x35, 0xB4, 0x78, 0xFF);
    private static readonly SKColor weight6  = new(0x22, 0xA8, 0x84, 0xFF);
    private static readonly SKColor weight7  = new(0x25, 0x92, 0x89, 0xFF);
    private static readonly SKColor weight8  = new(0x28, 0x7D, 0x8E, 0xFF);
    private static readonly SKColor weight9  = new(0x31, 0x68, 0x8D, 0xFF);
    private static readonly SKColor weight10 = new(0x3A, 0x54, 0x8C, 0xFF);
    private static readonly SKColor weight11 = new(0x40, 0x41, 0x84, 0xFF);
    private static readonly SKColor weight12 = new(0x47, 0x2E, 0x7C, 0xFF);
    private static readonly SKColor weight13 = new(0x45, 0x1A, 0x67, 0xFF);
    private static readonly SKColor weight14 = new(0x44, 0x07, 0x52, 0xFF);
    private static readonly SKColor weight15 = new(0x29, 0x04, 0x31, 0xFF);
    private static SKColor GetWeightColor(float weight)
    {
        float cubicWeight = weight < 0.5f ? 4 * weight * weight * weight :  1 - MathF.Pow(-2 * weight + 2, 3) * 0.5f;
        return (cubicWeight) switch
        {
            >= 0.9375f => weight0,
            >= 0.875f  => weight1,
            >= 0.8125f => weight2,
            >= 0.75f   => weight3,
            >= 0.6875f => weight4,
            >= 0.625f  => weight5,
            >= 0.5625f => weight6,
            >= 0.5f    => weight7,
            >= 0.4375f => weight8,
            >= 0.375f  => weight9,
            >= 0.3125f => weight10,
            >= 0.25f   => weight11,
            >= 0.1875f => weight12,
            >= 0.125f  => weight13,
            >= 0.0625f => weight14,
            _ => weight15
        };
    }

    private readonly SKPaint thinOutlinePen = new()
    {
        Color = OutlineColor,
        StrokeWidth = 0.5f,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke
    };

    private readonly SKPaint mediumOutlinePen = new()
    {
        Color = OutlineColor,
        StrokeWidth = 1.0f,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke
    };

    private readonly SKPaint boldOutlinePen = new()
    {
        Color = OutlineColor,
        StrokeWidth = 1.5f,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke
    };

    private readonly SKPaint radarLinePen = new()
    {
        Color = SKColors.MediumSlateBlue,
        StrokeWidth = 1.5f,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke
    };

    private readonly SKPaint radarFillPen = new()
    {
        Color = SKColors.MediumSlateBlue.WithAlpha(0x80),
        IsAntialias = false,
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint radarDotPen = new()
    {
        Color = SKColors.MediumSlateBlue,
        IsAntialias = false,
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint radarDottedPen = new()
    {
        Color = OutlineColor,
        StrokeWidth = 0.5f,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        PathEffect = SKPathEffect.CreateDash([5, 5], 0)
    };

    private SKPaint weightPaint = new()
    {
        Style = SKPaintStyle.Fill,
        IsAntialias = false
    };
    private SKPaint GetWeightPaint(float weight)
    {
        weightPaint.Color = GetWeightColor(weight);
        return weightPaint;
    }

    private SKPoint canvasCenter = new(200, 200);

    private SKSize heatmapRadius0 = new(198, 198); // hacky bullshit so the circle isnt cut off by the canvas edge
    private SKSize heatmapRadius1 = new(175, 175);
    private SKSize heatmapRadius2 = new(155, 155);
    private SKSize heatmapRadius3 = new(135, 135);
    private SKSize heatmapRadius4 = new(115, 115);

    private SKSize radarRadius0 = new(198, 198);
    private SKSize radarRadius1 = new(120, 120);
    private SKSize radarRadius2 = new( 40,  40);

    public void RenderHeatmap(SKCanvas canvas, float[] weights)
    {
        canvas.Clear(BackgroundColor);
        
        var r0 = SKRect.Create(canvasCenter.X - heatmapRadius0.Width, canvasCenter.Y - heatmapRadius0.Height, heatmapRadius0.Width * 2, heatmapRadius0.Height * 2);
        var r1 = SKRect.Create(canvasCenter.X - heatmapRadius4.Width, canvasCenter.Y - heatmapRadius4.Height, heatmapRadius4.Width * 2, heatmapRadius4.Height * 2);

        for (int i = 0; i < 60; i++)
        {
            float angle = i * -6;

            float startAngle0 = angle;
            float startAngle1 = angle - 6;

            var path = new SKPath();
            path.ArcTo(r0, startAngle0, -6, false);
            path.ArcTo(r1, startAngle1,  6, false);

            canvas.DrawPath(path, GetWeightPaint(weights[i]));
        }

        for (int i = 0; i < 60; i++)
        {
            float angle = i * -6;

            SKPoint p0 = GetPointOnArc(canvasCenter.X, canvasCenter.Y, heatmapRadius4.Width, angle);
            SKPoint p1 = GetPointOnArc(canvasCenter.X, canvasCenter.Y, heatmapRadius0.Width, angle);

            if (i % 5 == 0)
            {
                canvas.DrawLine(p0, p1, mediumOutlinePen);
            }
            else
            {
                canvas.DrawLine(p0, p1, thinOutlinePen);
            }
        }

        canvas.DrawOval(canvasCenter, heatmapRadius0, boldOutlinePen);
        canvas.DrawOval(canvasCenter, heatmapRadius1, thinOutlinePen);
        canvas.DrawOval(canvasCenter, heatmapRadius2, thinOutlinePen);
        canvas.DrawOval(canvasCenter, heatmapRadius3, thinOutlinePen);
        canvas.DrawOval(canvasCenter, heatmapRadius4, boldOutlinePen);
    }

    public void RenderSkillRadar(SKCanvas canvas, float[] values)
    {
        canvas.Clear(BackgroundColor);
        canvas.DrawOval(canvasCenter, radarRadius0, radarDottedPen);
        canvas.DrawOval(canvasCenter, radarRadius1, radarDottedPen);
        canvas.DrawOval(canvasCenter, radarRadius2, radarDottedPen);
        for (int i = 0; i < values.Length * 2; i++)
        {
            float angle = i * (180 / values.Length);

            SKPoint p0 = GetPointOnArc(canvasCenter.X, canvasCenter.Y, radarRadius0.Width, angle);
            SKPoint p1 = GetPointOnArc(canvasCenter.X, canvasCenter.Y, radarRadius2.Width, angle);

            canvas.DrawLine(p0, p1, radarDottedPen);
        }

        var path = new SKPath();

        for (int i = 0; i < values.Length; i++)
        {
            float radius = (1 - values[i]) * radarRadius2.Width + values[i] * radarRadius0.Width;
            float angle = i * (360 / values.Length);
            SKPoint point = GetPointOnArc(canvasCenter.X, canvasCenter.Y, radius, angle);

            canvas.DrawCircle(point, 3, radarDotPen);
            
            if (i == 0)
            {
                path.MoveTo(point);
            }
            else
            {
                path.LineTo(point);
            }
        }

        path.Close();
        canvas.DrawPath(path, radarFillPen);
        canvas.DrawPath(path, radarLinePen);
    }

    private static SKPoint GetPointOnArc(float x, float y, float radius, float angle)
    {
        var point = new SKPoint(
            (float)(radius * Math.Cos(Deg2Rad(angle)) + x),
            (float)(radius * Math.Sin(Deg2Rad(angle)) + y));
        return point;
    }

    private static float Deg2Rad(float angle)
    {
        return (MathF.PI / 180) * angle;
    }
}
