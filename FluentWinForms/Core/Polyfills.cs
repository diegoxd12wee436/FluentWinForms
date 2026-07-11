using System.ComponentModel;

// Esto le enseña al .NET 4.8 cómo usar el 'init' moderno
namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}