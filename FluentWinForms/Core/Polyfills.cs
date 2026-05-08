using System.ComponentModel;
using System;
using System.Drawing;
using System.Windows.Forms;

// Esto le enseña al .NET 4.8 cómo usar el 'init' moderno
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
#if NETFRAMEWORK
namespace System
{
    // Esto le enseña a .NET 4.8 a generar Hashes como los modernos
    internal struct HashCode
    {
        private int _hash;
        public void Add<T>(T value) => _hash = (_hash * 31) + (value?.GetHashCode() ?? 0);
        public int ToHashCode() => _hash;

        // Compatibilidad para los métodos Add que usas
        public void Add(object value) => _hash = (_hash * 31) + (value?.GetHashCode() ?? 0);
    }
}
#endif