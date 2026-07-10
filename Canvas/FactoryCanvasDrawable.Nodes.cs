using Ficsit.Schematics.Core.Model;
using Ficsit.Schematics.Core.Numerics;
using Ficsit.Schematics.Core.Saves;
using Ficsit.Schematics.Core.Solver;
using Ficsit.Schematics.Services;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Ficsit.Schematics.Canvas;

public sealed partial class FactoryCanvasDrawable
{
    // ------------------------------------------------------------------ nodes

    private void DrawNode(ICanvas canvas, NodeLayout layout, SolveResult result)
    {
        var node = layout.Node;
        var selected = state.Selection.Contains(node);
        var nodeResult = result.For(node);

        const float corner = 8f;

        if (node.Kind is NodeKind.Outpost or NodeKind.Blueprint)
        {
            canvas.FillColor = Theme.CardBackground;
            canvas.FillRoundedRectangle(layout.Bounds, corner);
            canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
            canvas.StrokeSize = selected ? 2f : 1f;
            canvas.DrawRoundedRectangle(layout.Bounds, corner);
            var icon = icons.GetImage(node.Kind == NodeKind.Outpost ? "Outpost" : "Blueprint");
            if (icon is not null) canvas.DrawImage(icon, layout.ImageRect.X, layout.ImageRect.Y, layout.ImageRect.Width, layout.ImageRect.Height);
            // Part icons on the boundary ports, same as machine cards, so it's clear what
            // flows in (left) and out (right).
            foreach (var port in layout.Inputs)
                DrawPort(canvas, layout, port, nodeResult);
            foreach (var port in layout.Outputs)
                DrawPort(canvas, layout, port, nodeResult);
            DrawTitle(canvas, layout, node);
            return;
        }

        if (layout.MapCompact)
        {
            DrawMapCompact(canvas, layout, nodeResult, selected);
            return;
        }

        // Card.
        canvas.FillColor = Theme.CardBackground;
        canvas.FillRoundedRectangle(layout.Bounds, corner);
        canvas.FillColor = Theme.ValueRowBackground;
        canvas.FillRectangle(layout.ValueRect);
        canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
        canvas.StrokeSize = selected ? 2f : 1f;
        canvas.DrawRoundedRectangle(layout.Bounds, corner);

        // Machine artwork (or name fallback).
        var machineImage = icons.GetImage(MachineImageName(node));
        if (machineImage is not null)
        {
            var rect = FitRect(layout.ImageRect, machineImage.Width / machineImage.Height);
            canvas.DrawImage(machineImage, rect.X, rect.Y, rect.Width, rect.Height);
        }
        else
        {
            canvas.FontColor = Theme.MutedText;
            canvas.FontSize = 9f;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.DrawString(node.Name, layout.ImageRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Ports.
        foreach (var port in layout.Inputs)
            DrawPort(canvas, layout, port, nodeResult);
        foreach (var port in layout.Outputs)
            DrawPort(canvas, layout, port, nodeResult);

        // Calculated value: machine count, or "ppm /min" — the unit beats the
        // reference's italics at telling the two modes apart.
        if (layout.HasValueRow)
        {
            canvas.FontColor = nodeResult.IsInvalid && state.Settings.FlagInvalidValues
                ? Theme.InvalidText
                : Theme.Text;
            canvas.FontSize = 11f;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var valueText = nodeResult.IsPpmDisplay
                ? numbers.Value(nodeResult.DisplayValue) + "/min"
                : numbers.Value(nodeResult.DisplayValue);
            canvas.DrawString(valueText,
                layout.ValueRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        // Limit box ("≤" marks it as a cap; empty box invites a click to set one).
        if (layout.HasLimitRow)
        {
            canvas.FillColor = Theme.LimitBoxBackground;
            canvas.FillRoundedRectangle(layout.LimitRect, 4f);
            canvas.StrokeColor = Theme.CardBorder;
            canvas.StrokeSize = 0.8f;
            canvas.DrawRoundedRectangle(layout.LimitRect, 4f);
            if (node.HasLimit)
            {
                canvas.FontColor = Theme.Text;
                canvas.FontSize = 10f;
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.DrawString("≤ " + node.Max!, layout.LimitRect, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        DrawTitle(canvas, layout, node);

        // Somersloop badge.
        if (node.Somersloops > 0)
        {
            var sloop = icons.GetUiImage("somersloop");
            if (sloop is not null)
                canvas.DrawImage(sloop, layout.Bounds.Right - 14, layout.Bounds.Top + 2, 12, 12);
        }
    }

    /// <summary>
    /// Compact map badge: rounded marker-sized card, machine icon, one condensed
    /// value chip (hidden when zoomed too far out — shown on hover instead), and the
    /// lone output port. Same selection ring as the full card.
    /// </summary>
    private void DrawMapCompact(ICanvas canvas, NodeLayout layout, NodeResult nodeResult, bool selected)
    {
        const float corner = 7f;
        var node = layout.Node;
        var showChip = Zoom >= NodeLayout.MapCompactChipZoomThreshold;

        canvas.FillColor = Theme.CardBackground;
        canvas.FillRoundedRectangle(layout.Bounds, corner);
        if (showChip)
        {
            canvas.FillColor = Theme.ValueRowBackground;
            canvas.FillRectangle(layout.ValueRect);
        }
        canvas.StrokeColor = selected ? Theme.SelectedBorder : Theme.CardBorder;
        canvas.StrokeSize = selected ? 2f : 1f;
        canvas.DrawRoundedRectangle(layout.Bounds, corner);

        // Machine artwork sits above the chip; leave the chip band clear when shown.
        var iconArea = showChip
            ? new RectF(layout.ImageRect.X, layout.ImageRect.Y,
                layout.ImageRect.Width, layout.Bounds.Bottom - NodeLayout.MapCompactChipHeight - layout.ImageRect.Y)
            : layout.ImageRect;
        var machineImage = icons.GetImage(MachineImageName(node));
        if (machineImage is not null)
        {
            var rect = FitRect(iconArea, machineImage.Width / machineImage.Height);
            canvas.DrawImage(machineImage, rect.X, rect.Y, rect.Width, rect.Height);
        }

        foreach (var port in layout.Outputs)
            DrawCompactPort(canvas, port, nodeResult);

        if (showChip)
        {
            canvas.FontColor = nodeResult.IsInvalid && state.Settings.FlagInvalidValues
                ? Theme.InvalidText
                : Theme.Text;
            canvas.FontSize = 9.5f;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var valueText = nodeResult.IsPpmDisplay
                ? numbers.Value(nodeResult.DisplayValue) + "/min"
                : numbers.Value(nodeResult.DisplayValue);
            canvas.DrawString(valueText, layout.ValueRect, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        DrawTitle(canvas, layout, node);
    }

    /// <summary>The badge's single output port: a small chip centered in its padded hit rect.</summary>
    private void DrawCompactPort(ICanvas canvas, PortInfo port, NodeResult nodeResult)
    {
        nodeResult.Outputs.TryGetValue(port.Part, out var portResult);
        const float visual = 16f;
        var rect = new RectF(
            port.IconRect.Center.X - visual / 2,
            port.IconRect.Center.Y - visual / 2,
            visual, visual);

        var chip = Theme.PortChip;
        if (portResult is not null && portResult.Unused.IsPositive && portResult.Target.IsPositive)
            chip = Theme.UnusedFlag;
        canvas.FillColor = chip;
        canvas.FillRoundedRectangle(rect, 4f);

        var icon = icons.GetImage(port.Part);
        if (icon is not null)
            canvas.DrawImage(icon, rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2);
    }

    private void DrawTitle(ICanvas canvas, NodeLayout layout, FactoryNode node)
    {
        var title = node.Title ?? (node.Kind is NodeKind.Outpost or NodeKind.Blueprint ? null : null);
        if (string.IsNullOrEmpty(title)) return;
        canvas.FontColor = Theme.Text;
        canvas.FontSize = 10f;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(title, layout.Bounds.X - 40, layout.Bounds.Y - 16, layout.Bounds.Width + 80, 14,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private void DrawPort(ICanvas canvas, NodeLayout layout, PortInfo port, NodeResult nodeResult)
    {
        var ports = port.IsInput ? nodeResult.Inputs : nodeResult.Outputs;
        ports.TryGetValue(port.Part, out var portResult);

        // Port chip; flag color when an input is undersupplied or an output has surplus.
        var chip = Theme.PortChip;
        if (portResult is not null)
        {
            if (port.IsInput && portResult.Unmade.IsPositive)
                chip = Theme.UnmadeFlag;
            else if (!port.IsInput && portResult.Unused.IsPositive && portResult.Target.IsPositive)
                chip = Theme.UnusedFlag;
        }
        canvas.FillColor = chip;
        canvas.FillRoundedRectangle(port.IconRect, 5f);

        var icon = icons.GetImage(port.Part);
        if (icon is not null)
            canvas.DrawImage(icon, port.IconRect.X + 1, port.IconRect.Y + 1, port.IconRect.Width - 2, port.IconRect.Height - 2);

        // ppm label outside the card — rendered as a measured pill (Slice B #3).
        // Hidden when zoomed out far enough that pills would collide with neighbours;
        // hover still surfaces the value via the tooltip path (TooltipTextAt covers port rects).
        if (portResult is not null && portResult.Target.IsPositive
            && Zoom >= NodeLayout.LabelHideZoomThreshold)
        {
            var text = numbers.Connection(portResult.Target);
            var labelCenterY = port.IconRect.Center.Y;
            if (port.IsInput)
            {
                // Pill sits to the left of the port chip with a 2-unit gap.
                var anchorRight = port.IconRect.Left - 2f;
                DrawLabelPill(canvas, text, anchorRight, labelCenterY, anchorRight: true);
            }
            else
            {
                // Pill sits to the right of the port chip with a 2-unit gap.
                var anchorLeft = port.IconRect.Right + 2f;
                DrawLabelPill(canvas, text, anchorLeft, labelCenterY, anchorRight: false);
            }
        }
    }

    public string MachineImageName(FactoryNode node)
    {
        switch (node.Kind)
        {
            case NodeKind.Recipe:
                if (state.Data.RecipesByName.TryGetValue(node.Name, out var recipe))
                {
                    var machine = recipe.Machine;
                    if (state.Data.MultiMachinesByName.TryGetValue(machine, out var family)
                        && family.Machines.Count > 0)
                    {
                        var variant = family.Machines.FirstOrDefault(v => v.Name == node.MachineVariant)
                            ?? family.Machines.FirstOrDefault(v => v.Default)
                            ?? family.Machines[0];
                        return variant.Name;
                    }
                    return machine;
                }
                return node.Name;
            case NodeKind.AwesomeSink: return "AWESOME Sink";
            case NodeKind.StorageContainer: return "Storage Container";
            case NodeKind.DimensionalDepot: return "Dimensional Depot Uploader";
            case NodeKind.Generator: return node.Name; // the machine, e.g. "Fuel-Powered Generator"
            default: return node.Name;
        }
    }

    private static RectF FitRect(RectF area, float aspect)
    {
        if (float.IsNaN(aspect) || aspect <= 0) aspect = 1;
        var width = area.Width;
        var height = width / aspect;
        if (height > area.Height)
        {
            height = area.Height;
            width = height * aspect;
        }
        return new RectF(area.Center.X - width / 2, area.Center.Y - height / 2, width, height);
    }
}
