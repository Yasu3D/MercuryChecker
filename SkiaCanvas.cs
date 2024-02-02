﻿using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace MercuryChecker.Controls;

public class SkiaCanvas : UserControl
{
    public event Action<SKCanvas>? RenderSkia;

    public override void Render(DrawingContext context)
    {
        if (RenderSkia != null)
            context.Custom(new SkiaDrawOp(new Rect(0, 0, DesiredSize.Width, DesiredSize.Height), RenderSkia));
        Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background);
    }

    private class SkiaDrawOp(Rect bounds, Action<SKCanvas> render) : ICustomDrawOperation
    {
        private readonly Action<SKCanvas> renderFunc = render;

        public void Dispose() { }

        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p)
        {
            return false;
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return false;
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
                return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            renderFunc.Invoke(canvas);
        }
    }
}