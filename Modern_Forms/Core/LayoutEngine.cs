using System;
using System.Drawing;
using System.Windows.Forms;

namespace Modern_Forms.Core
{
    public static class LayoutEngine
    {
        /// <summary>
        /// Calcula y asigna automáticamente el RectangleF (Layout) de todos los hijos en el árbol.
        /// </summary>
        public static void ComputeLayout(RenderNode node, RectangleF availableSpace)
        {
            // 🔥 CORRECCIÓN RESPONSIVE: Comprobamos si el nodo requiere estirarse (1fr/Stretch) permanentemente.
            // Ya no usamos IsNaN, porque eso se perdía en la primera pasada. Ahora el nodo NUNCA olvida que debe estirarse.
            float myWidth = node.StretchX ? availableSpace.Width : node.Layout.Width;
            float myHeight = node.StretchY ? availableSpace.Height : node.Layout.Height;

            // 2. Restricciones Min y Max (El nodo actual respeta sus propios límites)
            myWidth = Math.Max(node.MinSize.Width, node.MaxSize.Width > 0 ? Math.Min(myWidth, node.MaxSize.Width) : myWidth);
            myHeight = Math.Max(node.MinSize.Height, node.MaxSize.Height > 0 ? Math.Min(myHeight, node.MaxSize.Height) : myHeight);

            node.Layout = new RectangleF(availableSpace.X, availableSpace.Y, myWidth, myHeight);

            // Si no tiene hijos, no hay nada más que calcular
            if (node.Children.Count == 0) return;

            // 3. Calcular el espacio interno real (restando el Padding del padre)
            float innerX = node.Layout.X + node.Padding.Left;
            float innerY = node.Layout.Y + node.Padding.Top;
            float innerW = node.Layout.Width - (node.Padding.Left + node.Padding.Right);
            float innerH = node.Layout.Height - (node.Padding.Top + node.Padding.Bottom);

            // ==========================================
            // DISTRIBUCIÓN DE HIJOS (Según LayoutMode)
            // ==========================================

            if (node.LayoutMode == LayoutStyle.Absolute)
            {
                // En modo absoluto, los hijos ignoran el layout automático. 
                // Solo les pasamos el contenedor por si quieren anclarse, pero mantienen su X,Y.
                foreach (var child in node.Children)
                {
                    ComputeLayout(child, new RectangleF(innerX + child.Layout.X, innerY + child.Layout.Y, child.Layout.Width, child.Layout.Height));
                }
            }
            else if (node.LayoutMode == LayoutStyle.VerticalStack)
            {
                float currentY = innerY;
                foreach (var child in node.Children)
                {
                    if (!child.IsVisible) continue;

                    // El hijo hereda el ancho interno (si tiene StretchX) y calcula su alto
                    float childW = child.StretchX ? innerW : child.Layout.Width;
                    float childH = child.StretchY ? innerH : child.Layout.Height;

                    if (child.MinSize.Height > 0) childH = Math.Max(childH, child.MinSize.Height);

                    var childRect = new RectangleF(innerX, currentY, childW, childH);
                    ComputeLayout(child, childRect);

                    currentY += child.Layout.Height + node.Spacing;
                }
            }
            else if (node.LayoutMode == LayoutStyle.HorizontalStack)
            {
                float currentX = innerX;
                foreach (var child in node.Children)
                {
                    if (!child.IsVisible) continue;

                    // El hijo calcula su ancho, pero hereda el alto interno (si tiene StretchY)
                    float childW = child.StretchX ? innerW : child.Layout.Width;
                    float childH = child.StretchY ? innerH : child.Layout.Height;

                    if (child.MinSize.Width > 0) childW = Math.Max(childW, child.MinSize.Width);

                    var childRect = new RectangleF(currentX, innerY, childW, childH);
                    ComputeLayout(child, childRect);

                    currentX += child.Layout.Width + node.Spacing;
                }
            }
            else if (node.LayoutMode == LayoutStyle.AutoFitGrid)
            {
                float minColW = Math.Max(1f, node.GridMinColumnWidth);
                int columns = Math.Max(1, (int)((innerW + node.Spacing) / (minColW + node.Spacing)));
                float actualColW = (innerW - ((columns - 1) * node.Spacing)) / columns;

                float currentX = innerX;
                float currentY = innerY;

                // 🔥 FIX PRO: Agrupación por filas para calcular la altura real (StretchY)
                var rowNodes = new System.Collections.Generic.List<RenderNode>();

                Action layoutRow = () =>
                {
                    if (rowNodes.Count == 0) return;

                    // Encontrar el hijo más alto de la fila actual
                    float maxH = 0f;
                    foreach (var c in rowNodes)
                    {
                        float h = c.Layout.Height;
                        if (c.MinSize.Height > 0) h = Math.Max(h, c.MinSize.Height);
                        maxH = Math.Max(maxH, h);
                    }

                    // Aplicar layout estirando verticalmente si es necesario
                    float tempX = currentX;
                    foreach (var c in rowNodes)
                    {
                        float childW = actualColW;
                        if (c.MaxSize.Width > 0) childW = Math.Min(childW, c.MaxSize.Width);
                        float childH = c.StretchY ? maxH : Math.Max(c.Layout.Height, c.MinSize.Height);

                        ComputeLayout(c, new RectangleF(tempX, currentY, childW, childH));
                        tempX += actualColW + node.Spacing;
                    }
                    currentY += maxH + node.Spacing;
                    currentX = innerX;
                    rowNodes.Clear();
                };

                foreach (var child in node.Children)
                {
                    if (!child.IsVisible) continue;
                    rowNodes.Add(child);
                    if (rowNodes.Count >= columns) layoutRow();
                }
                layoutRow(); // Renderizar hijos restantes
            }
        }
    }
}