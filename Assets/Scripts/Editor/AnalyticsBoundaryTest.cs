using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Guarda arquitectónica: el núcleo de simulación (Gameplay/Electrical/Interaction/Player —
/// lo que hace que los 4 retos sean jugables) NO debe depender en tiempo de compilación de la
/// capa de analítica/LMS (AnalyticsManager, SessionDataExporter, DashboardServer,
/// DashboardBootstrap, TelemetryPublisher, PerformanceLogger, PerformanceBootstrap). Si el LMS
/// se reescribe, se rompe o se desactiva, los retos deben poder seguir compilando y jugándose.
///
/// Por qué es un escaneo de texto y no una separación por asmdef: hoy todo el proyecto compila
/// en un único Assembly-CSharp (confirmado — no hay asmdef propio, solo los vendorizados de
/// paquetes/SDKs), así que no hay límite de compilación real todavía. Este test es la guardia
/// que existe MIENTRAS tanto: si alguien agrega una referencia indebida, esto falla de inmediato
/// en el Test Runner en vez de descubrirse semanas después al tocar el LMS. El paso siguiente
/// (fuera de este test) es mover el núcleo a su propio .asmdef para que la garantía la imponga
/// el compilador, no un grep.
///
/// PerformanceTracker.cs vive físicamente en Gameplay/ y SÍ es referenciado por GameManager,
/// ObjectiveSystem y TechnicianActions (campo público `performance`) — pero es intencional: es
/// el colector de métricas que la propia jugabilidad alimenta en vivo (AddError, etc.), no la
/// capa de exportación/dashboard. Por eso NO está en la lista de identificadores prohibidos.
///
/// Única excepción real hoy: TestSupabaseSender.cs (en Gameplay/ por descuido) es un botón de
/// prueba manual para disparar un envío a Supabase — no es parte del loop jugable de ningún
/// reto, así que se excluye explícitamente en vez de fingir que no existe.
/// </summary>
public class AnalyticsBoundaryTest
{
    static readonly string[] NucleoDeSimulacion =
    {
        "Gameplay", "Electrical", "Interaction", "Player",
    };

    static readonly HashSet<string> ExcepcionesConocidas = new HashSet<string>
    {
        "TestSupabaseSender.cs",
    };

    static readonly string[] IdentificadoresDeAnalyticsProhibidos =
    {
        "AnalyticsManager",
        "SessionDataExporter",
        "DashboardServer",
        "DashboardBootstrap",
        "TelemetryPublisher",
        "PerformanceLogger",
        "PerformanceBootstrap",
    };

    [Test]
    public void NucleoDeSimulacion_NoReferenciaLaCapaDeAnalyticsOLms()
    {
        var offenders = new List<string>();

        foreach (var carpeta in NucleoDeSimulacion)
        {
            string abs = Path.Combine(Application.dataPath, "Scripts", carpeta);
            if (!Directory.Exists(abs)) continue;

            foreach (string file in Directory.GetFiles(abs, "*.cs", SearchOption.AllDirectories))
            {
                string nombre = Path.GetFileName(file);
                if (ExcepcionesConocidas.Contains(nombre)) continue;

                string texto = StripComentarios(File.ReadAllText(file));

                foreach (string id in IdentificadoresDeAnalyticsProhibidos)
                {
                    if (Regex.IsMatch(texto, $@"\b{id}\b"))
                        offenders.Add($"Scripts/{carpeta}/.../{nombre} referencia '{id}'");
                }
            }
        }

        Assert.IsEmpty(offenders,
            "El núcleo de simulación (Gameplay/Electrical/Interaction/Player) referencia tipos de " +
            "la capa de analítica/LMS. Esto significa que quitar o cambiar el LMS podría romper la " +
            "compilación de los retos. Hallazgos:\n" + string.Join("\n", offenders));
    }

    // Quita comentarios de línea (//...) y de bloque (/*...*/) para no confundir una mención en
    // un comentario o tooltip (permitida, es solo texto explicativo) con una referencia de código
    // real al tipo. No es un parser de C# completo: para los identificadores PascalCase que este
    // test busca, es más que suficiente.
    static string StripComentarios(string src)
    {
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        src = Regex.Replace(src, @"//.*?$", "", RegexOptions.Multiline);
        return src;
    }
}
