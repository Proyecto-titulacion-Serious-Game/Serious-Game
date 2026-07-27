using NUnit.Framework;

/// <summary>
/// Cobertura EditMode del algoritmo de calificación REAL del proyecto —
/// <see cref="PerformanceTracker.CalcularNota10"/> (5 pts por tiempo + 5 pts por errores,
/// tope de 4.0 si el reto no se completó) — llamado directo, sin mocks ni una copia
/// paralela de la fórmula. Ver el doc-comment del propio método para la especificación.
/// </summary>
public class PerformanceTrackerTests
{
    const float Limite = 600f;   // límite típico de un reto (Reto 1/2/3); tOptimo = 180 s

    [Test]
    public void SinErrores_DentroDelTiempoOptimo_NotaMaxima()
    {
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 100f, errores: 0, exito: true, limiteSeg: Limite);
        Assert.AreEqual(10.0f, nota, 1e-4f);
    }

    /// <summary>Mismo caso que documenta el doc-comment del método: "150 s y 1 error → 9.0".</summary>
    [Test]
    public void ConUnError_DentroDelTiempoOptimo_RestaUnPuntoPorError()
    {
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 150f, errores: 1, exito: true, limiteSeg: Limite);
        Assert.AreEqual(9.0f, nota, 1e-4f);
    }

    [Test]
    public void TiempoAMitadEntreOptimoYLimite_DecaeLinealmente()
    {
        // tOptimo=180, límite=600 → punto medio del tramo de decaimiento = 390 s → fTiempo=0.5
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 390f, errores: 0, exito: true, limiteSeg: Limite);
        Assert.AreEqual(7.5f, nota, 1e-4f);
    }

    /// <summary>
    /// Regresión del cambio de diseño reciente: el timer YA NO fuerza el fin del reto al
    /// agotarse — solo deja de sumar puntos por tiempo. Un reto completado justo al agotar
    /// (o después de) el límite de referencia debe seguir dando los 5 puntos de errores,
    /// nunca menos, y nunca forzar el tope de 4.0 (ese tope es solo para exito=false).
    /// </summary>
    [Test]
    public void TiempoAgotado_SoloPierdeLosPuntosDeTiempo_NoElTopeDeFallo()
    {
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 600f, errores: 0, exito: true, limiteSeg: Limite);
        Assert.AreEqual(5.0f, nota, 1e-4f);

        // Mucho más allá del límite: fTiempo se clampea en 0, no se vuelve negativo.
        float notaMuyTarde = PerformanceTracker.CalcularNota10(tiempoSeg: 5000f, errores: 0, exito: true, limiteSeg: Limite);
        Assert.AreEqual(5.0f, notaMuyTarde, 1e-4f);
    }

    [Test]
    public void RetoNoCompletado_NotaSeTopaEnCuatro_AunqueElRestoDeCuentasDeMasAlto()
    {
        // Tiempo y errores perfectos (darían 10.0), pero exito=false → tope 4.0.
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 50f, errores: 0, exito: false, limiteSeg: Limite);
        Assert.AreEqual(4.0f, nota, 1e-4f);
    }

    [Test]
    public void MuchosErrores_PuntosDeErrorNoBajanDeCero()
    {
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 100f, errores: 10, exito: true, limiteSeg: Limite);
        Assert.AreEqual(5.0f, nota, 1e-4f);   // 5 (tiempo) + max(0, 5-10)=0
    }

    [Test]
    public void LimiteSegInvalido_UsaDefaultDeSeiscientos()
    {
        // limiteSeg <= 0 debe comportarse exactamente como limiteSeg = 600 (mismo caso que
        // ConUnError_DentroDelTiempoOptimo_RestaUnPuntoPorError, pero sin pasar el límite).
        float nota = PerformanceTracker.CalcularNota10(tiempoSeg: 150f, errores: 1, exito: true, limiteSeg: 0f);
        Assert.AreEqual(9.0f, nota, 1e-4f);
    }
}
