using UnityEngine;

/// <summary>
/// Contiene el manual técnico completo para cada reto.
/// El Técnico lo consulta en su panel para diagnosticar y guiar al Explorador.
/// Incluye: concepto, fórmula, objetivo, tabla de valores y pistas.
/// </summary>
public class TechnicianManual : MonoBehaviour
{
    public GameManager gameManager;

    public ManualData GetManualData(LevelType level)
    {
        return level switch
        {
            LevelType.OhmLaw  => ManualReto1(),
            LevelType.Parallel=> ManualReto2(),
            LevelType.Mixed   => ManualReto3(),
            LevelType.Arduino => ManualReto4(),
            _                 => new ManualData { titulo = "Sin manual disponible" }
        };
    }

    // ─────────────────────────────────────────────
    //  RETO 1 — Ley de Ohm
    // ─────────────────────────────────────────────
    ManualData ManualReto1() => new ManualData
    {
        titulo    = "RETO 1 — Circuito Serie & Ley de Ohm",

        concepto  = "Un circuito serie conecta los componentes en cadena.\n" +
                    "La misma corriente I fluye por todos los componentes.\n" +
                    "El voltaje total se divide en caídas proporcionales a cada R.",

        formula   = "Ley de Ohm:     V = I x R\n" +
                    "Corriente:       I = V / R_total\n" +
                    "R total serie:   R_t = R1 + R2 + ...\n" +
                    "-------------------------\n" +
                    "Ejemplo con tus datos:\n" +
                    "R_necesaria = (V_fuente - V_LED) / I_LED\n" +
                    "I_LED correcto: 0.005A a 0.020A",

        objetivo  = "El LED está en sobrecarga (rojo) porque la\n" +
                    "resistencia tiene el valor incorrecto.\n\n" +
                    "1. Pide al Explorador que mida V en nodo_A y nodo_B\n" +
                    "2. Calcula: R = (9V - V_LED) / I_objetivo\n" +
                    "   (usa el voltaje medido; I_objetivo está en la fórmula)\n" +
                    "3. Escribe TU resultado en el campo y pulsa ENVIAR\n" +
                    "   Si el LED no cambia a verde, revisa el cálculo.",

        tablaValores =
                    "CÓDIGO DE COLORES DE RESISTENCIAS (4 bandas)\n" +
                    "-------------------------\n" +
                    "0 Negro    1 Marrón   2 Rojo     3 Naranja  4 Amarillo\n" +
                    "5 Verde    6 Azul     7 Violeta  8 Gris     9 Blanco\n" +
                    "Banda 3 (multiplicador): mismo color = x10^dígito\n" +
                    "Banda 4 (tolerancia): Oro=±5%  Plata=±10%\n" +
                    "-------------------------\n" +
                    "Resistencia con falla en la nave: 10 Ohm\n" +
                    "-> Bandas: Marrón-Negro-Negro-Oro\n\n" +
                    "Usá esta tabla para leer o escribir cualquier valor\n" +
                    "que calcules (acá y en otros retos)."
    };

    // ─────────────────────────────────────────────
    //  RETO 2 — Circuito Paralelo
    // ─────────────────────────────────────────────
    ManualData ManualReto2() => new ManualData
    {
        titulo    = "RETO 2 — Circuito Paralelo & Polaridad del LED",

        concepto  = "En paralelo, cada rama recibe el MISMO voltaje.\n" +
                    "El sensor (LED) es un DIODO: solo conduce en un sentido.\n" +
                    "Si su polaridad está INVERTIDA no pasa corriente -> apagado.",

        formula   = "POLARIDAD DEL LED (diodo):\n" +
                    "  Ánodo (+, pata larga) -> al voltaje positivo\n" +
                    "  Cátodo (-, pata corta / banda plana) -> a tierra\n\n" +
                    "Cada rama lleva una resistencia de protección en serie,\n" +
                    "así el LED enciende SEGURO (verde) sin quemarse:\n" +
                    "  I_rama = (V_fuente - V_LED) / (R_protección + R_LED)",

        objetivo  = "El sensor (LED) de una rama NO enciende porque está\n" +
                    "colocado con la POLARIDAD INVERTIDA (dañado).\n\n" +
                    "1. El Explorador conecta los cables de cada rama\n" +
                    "   (VCC -> resistencia -> LED -> GND) -> enciende el LED\n" +
                    "   bueno.\n" +
                    "2. Pide al Explorador medir el LED dañado (0V / sin\n" +
                    "   corriente = está al revés).\n" +
                    "3. Envía un LED NUEVO al Explorador.\n" +
                    "4. El Explorador REEMPLAZA el LED dañado colocando el\n" +
                    "   LED nuevo donde estaba: al soltarlo se conecta y\n" +
                    "   enciende solo (polaridad correcta).\n" +
                    "5. Los 2 LED en VERDE -> reto superado.",

        tablaValores =
                    "ESTADO DEL SENSOR SEGÚN POLARIDAD\n" +
                    "-------------------------\n" +
                    "INVERTIDA:  I ~ 0 A · LED apagado (negro)\n" +
                    "CORRECTA:   I ~ 10–15 mA · LED VERDE seguro\n" +
                    "-------------------------\n" +
                    "Condición de victoria: LED verde (polaridad correcta\n" +
                    "y corriente en rango seguro, sin sobrecarga).\n\n" +
                    "PRECAUCIÓN: si crees que está todo conectado y no\n" +
                    "marca victoria, presiona F8 para re-verificar el\n" +
                    "circuito (solo completa si de verdad está correcto)."
    };

    // ─────────────────────────────────────────────
    //  RETO 3 — Circuito Mixto & Polaridad
    // ─────────────────────────────────────────────
    ManualData ManualReto3() => new ManualData
    {
        titulo    = "RETO 3 — Circuito Mixto & Polaridad de Componentes",

        concepto  = "3 fallas simultáneas en el módulo de control:\n" +
                    "• LED con polaridad invertida -> no enciende\n" +
                    "• Capacitor electrolítico invertido -> cortocircuito\n" +
                    "• Resistencia con código de colores erróneo",

        formula   = "POLARIDAD LED:\n" +
                    "  Ánodo (+) -> al voltaje positivo\n" +
                    "  Cátodo (-) -> a tierra (banda plana o pata corta)\n\n" +
                    "POLARIDAD CAPACITOR electrolítico:\n" +
                    "  (+) banda blanca / pata larga -> positivo\n" +
                    "  (-) banda negra / pata corta -> tierra\n\n" +
                    "RESISTENCIA: no viene dada — calculala con la\n" +
                    "Ley de Ohm (ver Reto 1) y el código de colores\n" +
                    "(tabla abajo).",

        objetivo  = "Corregir las 3 fallas EN ORDEN DE PRIORIDAD, igual que en Retos 1 y 2:\n" +
                    "selecciona la pieza en tu panel y pulsa ENVIAR con la polaridad/valor correcto.\n\n" +
                    "PRIORIDAD 1 -> Capacitor (humo = riesgo crítico)\n" +
                    "  Selecciona Capacitor, polaridad CORRECTA, y ENVÍA\n\n" +
                    "PRIORIDAD 2 -> LED invertido\n" +
                    "  Selecciona LED, polaridad CORRECTA, y ENVÍA\n\n" +
                    "PRIORIDAD 3 -> Resistencia incorrecta\n" +
                    "  Calculá el valor correcto (Ley de Ohm + código\n" +
                    "  de colores) y ENVIALO",

        tablaValores =
                    "RESISTENCIA CON FALLA EN LA NAVE\n" +
                    "-------------------------\n" +
                    "Medida: 470 Ohm\n" +
                    "  Amarillo-Violeta-Marrón-Oro\n" +
                    "-------------------------\n" +
                    "Usá la tabla de código de colores del Reto 1 y la\n" +
                    "Ley de Ohm para calcular el valor correcto.\n\n" +
                    "Indicador de humo en capacitor:\n" +
                    "  -> corregir ANTES que el LED"
    };

    // ─────────────────────────────────────────────
    //  RETO 4 — Arduino + Protoboard
    // ─────────────────────────────────────────────
    ManualData ManualReto4() => new ManualData
    {
        titulo   = "RETO 4 — Sandbox Arduino + Protoboard",

        concepto =
            "Objetivo: hacer parpadear un LED de forma segura.\n\n" +
            "No hay fallas predefinidas. El equipo DISENHA\n" +
            "el circuito desde cero.\n\n" +
            "TECNICO (tu rol):\n" +
            "  Escribe el sketch y elige cualquier pin D2-D13.\n" +
            "  El LED debe parpadear (BLINK) sin quemarse.\n\n" +
            "EXPLORADOR (guialo):\n" +
            "  Toma LED + resistencia de la bandeja VR.\n" +
            "  Conecta: Pin elegido -> LED -> Resistencia -> GND.\n\n" +
            "El validador detecta automaticamente cuando el\n" +
            "circuito es correcto, sin importar que pin usaron.",

        formula =
            "QUE HACE CADA COMANDO:\n" +
            "-------------------------\n" +
            "pinMode(pin, OUTPUT)\n" +
            "   Configura el pin como SALIDA (manda\n" +
            "   corriente). Va en setup(). Para un LED\n" +
            "   SIEMPRE OUTPUT (INPUT = entrada, no sirve).\n" +
            "digitalWrite(pin, HIGH)\n" +
            "   Pin a 5V -> ENCIENDE el LED.\n" +
            "digitalWrite(pin, LOW)\n" +
            "   Pin a 0V -> APAGA el LED.\n" +
            "delay(ms)\n" +
            "   ESPERA (1000 ms = 1 s). Hace visible el\n" +
            "   parpadeo.\n" +
            "setup() corre 1 vez | loop() se repite siempre\n\n" +
            "COMO ELEGIR / VER EL PIN:\n" +
            "-------------------------\n" +
            "Los pines van rotulados D2..D13 en la placa\n" +
            "(el Explorador los ve en VR). El NUMERO que\n" +
            "escribas = el pin que se activa. Escribe 7 ->\n" +
            "se enciende D7. AVISA AL EXPLORADOR que pin\n" +
            "elegiste: el debe conectar el LED a ESE pin.\n\n" +
            "PASOS:\n" +
            "1. Clic en el monitor del PC_Arduino (abre IDE)\n" +
            "2. Escribe el sketch (reemplaza __ por tu pin):\n" +
            "     void setup() {\n" +
            "       pinMode(__, OUTPUT);\n" +
            "     }\n" +
            "     void loop() {\n" +
            "       digitalWrite(__, HIGH);\n" +
            "       delay(500);\n" +
            "       digitalWrite(__, LOW);\n" +
            "       delay(500);\n" +
            "     }\n" +
            "3. COMPILAR (Ctrl+Enter) -> consola:\n" +
            "     OK  Pin D__  OUTPUT  BLINK 500ms\n" +
            "4. SUBIR -> el pin queda activo en el Arduino",

        objetivo =
            "PASOS DEL RETO:\n\n" +
            "TECNICO:\n" +
            "  1. Abrir monitor del PC_Arduino\n" +
            "  2. Elegir un pin digital libre (D2–D13)\n" +
            "  3. Escribir sketch con OUTPUT + BLINK\n" +
            "  4. Compilar — revisar que diga OK\n" +
            "  5. Subir sketch al Arduino\n" +
            "  6. Comunicar al Explorador que pin elegiste\n\n" +
            "EXPLORADOR:\n" +
            "  7. Tomar LED de la bandeja VR\n" +
            "  8. Insertar anodo (+) en el pin indicado\n" +
            "  9. Conectar resistencia 330 Ohm en serie\n" +
            " 10. Cerrar circuito al GND del Arduino\n" +
            " 11. Presionar el boton fisico de validacion\n\n" +
            "VALIDACION EXITOSA:\n" +
            "  El DFS detecta: BLINK + LED + R>=100 + GND\n" +
            "  Boton VR verde + haptica = RETO COMPLETADO",

        tablaValores =
            "PINES DIGITALES DISPONIBLES:\n" +
            "-------------------------\n" +
            "D2  D3  D4  D5  D6  D7\n" +
            "D8  D9  D10 D11 D12 D13\n" +
            "  -> Cualquiera sirve · Evita D0 y D1 (RX/TX)\n\n" +
            "RESISTENCIA RECOMENDADA:\n" +
            "  330 Ohm = Naranja-Naranja-Marron-Oro\n" +
            "  R = (5V - 2V) / 0.01A = 300 -> usa 330 Ohm\n\n" +
            "HUD / TELEMETRIA — QUE SIGNIFICA CADA DATO:\n" +
            "-------------------------\n" +
            "  V   : voltaje en el pin activo (~5V en HIGH)\n" +
            "  I   : corriente en mA (pequena y estable)\n" +
            "  P   : potencia en W (consumo, debe ser bajo)\n" +
            "  ADC : sensor analogico A0, valor 0..1023\n" +
            "        (0V=0 ; 5V=1023)\n\n" +
            "ESTADO DEL SISTEMA (texto de color):\n" +
            "  VERDE  OPERACION SEGURA  -> objetivo OK\n" +
            "  ROJO   CORTOCIRCUITO     -> FALTA la\n" +
            "         resistencia o hay un corto\n" +
            "  NARANJA CIRCUITO ABIERTO (0 mA) -> cable\n" +
            "         suelto o no cierra a GND\n\n" +
            "COMO GUIAR AL EXPLORADOR CON EL HUD:\n" +
            "  Ves ROJO    -> 'revisa/agrega la resistencia'\n" +
            "  Ves 0 mA    -> 'revisa el cable a GND'\n" +
            "  Ves VERDE   -> 'cierra y valida'",

        programaReferencia =
            "EJEMPLO DE SKETCH — RETO 4:\n\n" +
            "// Cambia 7 por el pin que elijas\n" +
            "void setup() {\n" +
            "  pinMode(7, OUTPUT);\n" +
            "}\n\n" +
            "void loop() {\n" +
            "  digitalWrite(7, HIGH);\n" +
            "  delay(500);\n" +
            "  digitalWrite(7, LOW);\n" +
            "  delay(500);\n" +
            "}\n\n" +
            "ERRORES QUE DETECTA EL COMPILADOR:\n" +
            "  X  Sin OUTPUT -> dice modo INPUT\n" +
            "  X  Sin delay  -> no hay BLINK\n" +
            "  X  Pin 0 o 1  -> fuera de rango\n\n" +
            "CHECKLIST ANTES DE VALIDAR:\n" +
            "  [ ] Sketch subido (consola dice OK)\n" +
            "  [ ] LED en protoboard con polaridad OK\n" +
            "  [ ] Resistencia >= 100 Ohm en serie\n" +
            "  [ ] Circuito cerrado al GND\n" +
            "  [ ] Boton fisico VR presionado"
    };
}

[System.Serializable]
public struct ManualData
{
    public string titulo;
    public string concepto;
    public string formula;
    public string objetivo;
    public string tablaValores;
    public string programaReferencia; // página 3 — sketch de referencia y checklist
}