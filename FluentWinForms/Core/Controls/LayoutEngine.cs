using System;
using System.Buffers;
using System.Drawing;

namespace FluentWinForms.Core
{
    public static class LayoutEngine
    {
        /// <summary>
        /// Calcula y asigna automáticamente el RectangleF (Layout) de todos los hijos en el árbol.
        /// </summary>
        public static void ComputeLayout(RenderNode node, RectangleF availableSpace)
        {
            // Respetar StretchX/StretchY del nodo actual
            float myWidth = node.StretchX ? availableSpace.Width : node.Layout.Width;
            float myHeight = node.StretchY ? availableSpace.Height : node.Layout.Height;

            // Restricciones Min/Max
            myWidth = Math.Max(node.MinSize.Width, node.MaxSize.Width > 0 ? Math.Min(myWidth, node.MaxSize.Width) : myWidth);
            myHeight = Math.Max(node.MinSize.Height, node.MaxSize.Height > 0 ? Math.Min(myHeight, node.MaxSize.Height) : myHeight);

            node.Layout = new RectangleF(availableSpace.X, availableSpace.Y, myWidth, myHeight);

            if (node.Children.Count == 0) return;

            // Espacio interno (padding)
            float innerX = node.Layout.X + node.Padding.Left;
            float innerY = node.Layout.Y + node.Padding.Top;
            float innerW = node.Layout.Width - (node.Padding.Left + node.Padding.Right);
            float innerH = node.Layout.Height - (node.Padding.Top + node.Padding.Bottom);

            switch (node.LayoutMode)
            {
                case LayoutStyle.Absolute:
                    ComputeAbsolute(node, innerX, innerY);
                    break;

                case LayoutStyle.VerticalStack:
                    ComputeVerticalStack(node, innerX, innerY, innerW, innerH);
                    break;

                case LayoutStyle.HorizontalStack:
                    ComputeHorizontalStack(node, innerX, innerY, innerW, innerH);
                    break;

                case LayoutStyle.AutoFitGrid:
                    ComputeAutoFitGrid(node, innerX, innerY, innerW);
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // ABSOLUTE
        // ─────────────────────────────────────────────────────────────────

        private static void ComputeAbsolute(RenderNode node, float innerX, float innerY)
        {
            foreach (var child in node.Children)
            {
                ComputeLayout(child, new RectangleF(
                    innerX + child.Layout.X,
                    innerY + child.Layout.Y,
                    child.Layout.Width,
                    child.Layout.Height));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // VERTICAL STACK — FIX: dos pasadas para "fill remaining" real
        // ─────────────────────────────────────────────────────────────────

        private static void ComputeVerticalStack(
    RenderNode node, float innerX, float innerY, float innerW, float innerH)
        {
            int stretchCount = 0, visibleCount = 0;
            float fixedH = 0f;

            foreach (var child in node.Children)
            {
                if (!child.IsVisible) continue;
                visibleCount++;
                if (child.StretchY) stretchCount++;
                else
                {
                    float h = child.Layout.Height;
                    if (child.MinSize.Height > 0) h = Math.Max(h, child.MinSize.Height);
                    fixedH += h + child.Margin.Top + child.Margin.Bottom;
                }
            }

            float baseSpacing = visibleCount > 1 ? node.Spacing : 0f;
            float totalSpacing = (visibleCount - 1) * baseSpacing;
            float totalContentH = fixedH + totalSpacing;
            float stretchH = stretchCount > 0 ? Math.Max(0f, (innerH - fixedH - totalSpacing) / stretchCount) : 0f;

            float currentY = innerY;
            float dynamicSpacing = baseSpacing;

            switch (node.JustifyContent)
            {
                case Justify.Center: currentY = innerY + (innerH - totalContentH) / 2f; break;
                case Justify.End: currentY = innerY + innerH - totalContentH; break;
                case Justify.SpaceBetween when visibleCount > 1:
                    dynamicSpacing = (innerH - fixedH) / (visibleCount - 1); break;
                case Justify.SpaceAround:
                    dynamicSpacing = (innerH - fixedH) / Math.Max(1, visibleCount);
                    currentY = innerY + dynamicSpacing / 2f; break;
            }

            foreach (var child in node.Children)
            {
                if (!child.IsVisible) continue;

                float childW = child.StretchX ? innerW - child.Margin.Left - child.Margin.Right : child.Layout.Width;
                float childH = child.StretchY ? stretchH : child.Layout.Height;

                if (child.MinSize.Height > 0) childH = Math.Max(childH, child.MinSize.Height);
                if (child.MaxSize.Height > 0) childH = Math.Min(childH, child.MaxSize.Height);

                float childX = node.AlignItems switch
                {
                    Align.Center => innerX + (innerW - childW) / 2f,
                    Align.End => innerX + innerW - childW - child.Margin.Right,
                    _ => innerX + child.Margin.Left
                };

                currentY += child.Margin.Top;
                ComputeLayout(child, new RectangleF(childX, currentY, childW, childH));
                currentY += child.Layout.Height + child.Margin.Bottom + dynamicSpacing;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // HORIZONTAL STACK — FIX: dos pasadas para "fill remaining" real
        // ─────────────────────────────────────────────────────────────────

        private static void ComputeHorizontalStack(
        RenderNode node, float innerX, float innerY, float innerW, float innerH)
        {
            int stretchCount = 0, visibleCount = 0;
            float fixedW = 0f;

            foreach (var child in node.Children)
            {
                if (!child.IsVisible) continue;
                visibleCount++;
                if (child.StretchX) stretchCount++;
                else
                {
                    float w = child.Layout.Width;
                    if (child.MinSize.Width > 0) w = Math.Max(w, child.MinSize.Width);
                    fixedW += w + child.Margin.Left + child.Margin.Right;
                }
            }

            float baseSpacing = visibleCount > 1 ? node.Spacing : 0f;
            float totalSpacing = (visibleCount - 1) * baseSpacing;
            float totalContentW = fixedW + totalSpacing;
            float stretchW = stretchCount > 0 ? Math.Max(0f, (innerW - fixedW - totalSpacing) / stretchCount) : 0f;

            float currentX = innerX;
            float dynamicSpacing = baseSpacing;

            switch (node.JustifyContent)
            {
                case Justify.Center: currentX = innerX + (innerW - totalContentW) / 2f; break;
                case Justify.End: currentX = innerX + innerW - totalContentW; break;
                case Justify.SpaceBetween when visibleCount > 1:
                    dynamicSpacing = (innerW - fixedW) / (visibleCount - 1); break;
                case Justify.SpaceAround:
                    dynamicSpacing = (innerW - fixedW) / Math.Max(1, visibleCount);
                    currentX = innerX + dynamicSpacing / 2f; break;
            }

            foreach (var child in node.Children)
            {
                if (!child.IsVisible) continue;

                float childW = child.StretchX ? stretchW : child.Layout.Width;
                float childH = child.StretchY ? innerH - child.Margin.Top - child.Margin.Bottom : child.Layout.Height;

                if (child.MinSize.Width > 0) childW = Math.Max(childW, child.MinSize.Width);
                if (child.MaxSize.Width > 0) childW = Math.Min(childW, child.MaxSize.Width);

                float childY = node.AlignItems switch
                {
                    Align.Center => innerY + (innerH - childH) / 2f,
                    Align.End => innerY + innerH - childH - child.Margin.Bottom,
                    _ => innerY + child.Margin.Top
                };

                currentX += child.Margin.Left;
                ComputeLayout(child, new RectangleF(currentX, childY, childW, childH));
                currentX += child.Layout.Width + child.Margin.Right + dynamicSpacing;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // AUTO FIT GRID — FIX: ArrayPool + método estático, sin List ni lambda
        // ─────────────────────────────────────────────────────────────────

        private static void ComputeAutoFitGrid(
            RenderNode node,
            float innerX, float innerY,
            float innerW)
        {
            float minColW = Math.Max(1f, node.GridMinColumnWidth);
            int columns = Math.Max(1, (int)((innerW + node.Spacing) / (minColW + node.Spacing)));
            float actualColW = (innerW - ((columns - 1) * node.Spacing)) / columns;

            // Renta un buffer del pool en lugar de new List<RenderNode>()
            // ArrayPool.Rent puede devolver un array más grande — usamos rowCount para el tamaño real
            RenderNode[] rowBuffer = ArrayPool<RenderNode>.Shared.Rent(columns);
            int rowCount = 0;
            float rowY = innerY;

            try
            {
                foreach (var child in node.Children)
                {
                    if (!child.IsVisible) continue;

                    rowBuffer[rowCount++] = child;

                    if (rowCount >= columns)
                    {
                        rowY = FlushGridRow(rowBuffer, rowCount, innerX, rowY, actualColW, node.Spacing);
                        rowCount = 0;
                    }
                }

                // Última fila incompleta (si queda algo)
                if (rowCount > 0)
                    FlushGridRow(rowBuffer, rowCount, innerX, rowY, actualColW, node.Spacing);
            }
            finally
            {
                // clearArray: true limpia las referencias a RenderNode para el GC
                ArrayPool<RenderNode>.Shared.Return(rowBuffer, clearArray: true);
            }
        }

        /// <summary>
        /// Calcula la altura máxima de una fila, posiciona sus hijos y devuelve el Y de la siguiente fila.
        /// Método estático puro — sin capturas de closure, sin allocations.
        /// </summary>
        private static float FlushGridRow(
            RenderNode[] row, int count,
            float startX, float startY,
            float colW, float spacing)
        {
            // Altura máxima de la fila (respetando MinSize)
            float maxH = 0f;
            for (int i = 0; i < count; i++)
            {
                float h = row[i].Layout.Height;
                if (row[i].MinSize.Height > 0) h = Math.Max(h, row[i].MinSize.Height);
                maxH = Math.Max(maxH, h);
            }

            // Posicionar cada hijo de la fila
            float x = startX;
            for (int i = 0; i < count; i++)
            {
                var c = row[i];
                float childW = colW;
                float childH = c.StretchY ? maxH : Math.Max(c.Layout.Height, c.MinSize.Height);

                if (c.MaxSize.Width > 0) childW = Math.Min(childW, c.MaxSize.Width);
                if (c.MaxSize.Height > 0) childH = Math.Min(childH, c.MaxSize.Height);

                ComputeLayout(c, new RectangleF(x, startY, childW, childH));
                x += colW + spacing;
            }

            return startY + maxH + spacing;
        }
    }
}