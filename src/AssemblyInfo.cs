using System.Reflection;
using System.Runtime.Versioning;

// Sin esto el ejecutable se trata como de .NET 4.0 y no admite rutas de más de 260 caracteres
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

[assembly: AssemblyTitle("Optimizador de PC - Edición Halloween")]
[assembly: AssemblyProduct("Optimizador de PC")]
[assembly: AssemblyDescription("Libera espacio en el disco y controla los programas que saturan el PC")]
[assembly: AssemblyCopyright("Desarrollada por Biliman")]
[assembly: AssemblyCompany("Biliman")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

// Permite que las pruebas automáticas (PruebasOptimizador.exe) usen las clases internas
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PruebasOptimizador")]
