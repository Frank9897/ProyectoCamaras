using System.Runtime.CompilerServices;

// Permite que el proyecto de tests valide parsers y utilidades internas sin convertirlos en API pública de producción.
[assembly: InternalsVisibleTo("CameraInspector.Tests")]
