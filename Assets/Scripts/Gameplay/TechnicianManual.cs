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

        // La franja de corriente de abajo NO es decorativa: la pieza se acepta con una tolerancia
        // del 12% (Resistor.IsValueCorrect usa max(tolerancePercent, 12)), y con V_fuente=9V y
        // V_LED=2V solo un objetivo de ~7,5-8,5 mA cae dentro de esa ventana. El texto anterior
        // decia "0.005A a 0.020A": siguiendo el propio manual con un valor redondo y razonable
        // (10 o 15 mA) salia una resistencia que el juego rechazaba. Si se cambian los valores de
        // la escena (correctResistance / Vf / voltaje de la fuente), recalcular esta franja.
        formula   = "Ley de Ohm:     V = I x R\n" +
                    "Corriente:       I = V / R_total\n" +
                    "R total serie:   R_t = R1 + R2 + ...\n" +
                    "-------------------------\n" +
                    "RESISTENCIA LIMITADORA DE UN LED:\n" +
                    "  V_R = V_fuente - V_LED\n" +
                    "  R   = V_R / I_objetivo\n\n" +
                    "El LED es un DIODO: 'consume' su caida\n" +
                    "directa V_LED (~2 V) y el RESTO del\n" +
                    "voltaje lo absorbe la resistencia serie.\n" +
                    "-------------------------\n" +
                    "CORRIENTE NOMINAL DE ESTE LED:\n" +
                    "  8 mA  (franja util 7,5 a 8,5 mA,\n" +
                    "         o sea 0,0075 A a 0,0085 A)\n\n" +
                    "  OJO: 10 mA / 15 mA / 20 mA son de un\n" +
                    "  LED indicador generico. ESTE modulo no\n" +
                    "  acepta la pieza con esos objetivos.\n" +
                    "-------------------------\n" +
                    "EJEMPLO RESUELTO — son OTROS numeros,\n" +
                    "no los de esta nave; sirve solo para ver\n" +
                    "el metodo:\n" +
                    "  fuente 5 V · LED de 2 V · objetivo 6 mA\n" +
                    "  V_R = 5 - 2 = 3 V\n" +
                    "  R   = 3 V / 0,006 A = 500 Ohm\n\n" +
                    "Repeti esos 2 pasos con el voltaje que te\n" +
                    "dicte el Explorador y la corriente nominal\n" +
                    "de arriba.",

        objetivo  = "La resistencia serie tiene el valor\n" +
                    "equivocado: pasa demasiada corriente y el\n" +
                    "LED se ve ROJO (sobrecarga).\n\n" +
                    "1. Que el Explorador CIERRE el interruptor\n" +
                    "   de la mesa. Con el abierto todo mide 0.\n" +
                    "2. VOLTAJE entre los 2 bornes de la bateria\n" +
                    "   (disco VCC y disco GND): eso te da\n" +
                    "   V_fuente. Es el voltaje TOTAL del lazo,\n" +
                    "   no la caida de un componente suelto.\n" +
                    "3. Puntas en los 2 EXTREMOS de la\n" +
                    "   resistencia y boton de modo hasta\n" +
                    "   CORRIENTE: esa es la corriente real que\n" +
                    "   circula ahora. Comparala con la nominal\n" +
                    "   del LED (ver formulas): si es mucho\n" +
                    "   mayor, la resistencia es muy BAJA.\n" +
                    "4. Calcula R = (V_fuente - V_LED) / I_obj\n" +
                    "   con la corriente nominal del LED.\n" +
                    "5. Escribe TU resultado y pulsa ENVIAR.\n" +
                    "   Si el LED no queda verde, revisa el\n" +
                    "   paso 4: casi siempre es la corriente\n" +
                    "   objetivo mal elegida.",

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
                    "colocado con la POLARIDAD INVERTIDA (dañado) — esa\n" +
                    "pieza ya no sirve, hay que REEMPLAZARLA (no basta con\n" +
                    "voltearla a mano).\n\n" +
                    "1. El Explorador conecta los cables de AMBAS ramas\n" +
                    "   (batería -> riel VCC/GND, y en cada rama:\n" +
                    "   VCC -> resistencia -> LED -> GND). La rama sana\n" +
                    "   enciende sola en cuanto queda cerrada.\n" +
                    "2. Pide al Explorador medir el LED dañado (0V / sin\n" +
                    "   corriente = está al revés).\n" +
                    "3. Selecciona LED en tu panel, polaridad CORRECTA, y\n" +
                    "   ENVÍA — viaja como pieza SUELTA, no se instala solo.\n" +
                    "4. El Explorador debe soltarlo EXACTAMENTE sobre el\n" +
                    "   slot marcado de esa rama (fila 3, columna 5): ahí\n" +
                    "   encaja y queda fijo, ya con su propia protección\n" +
                    "   incluida (no hace falta otra resistencia para él).\n" +
                    "   En cualquier otro lugar sigue agarrable/suelto.\n" +
                    "5. Los 2 LED en VERDE -> reto superado.",

        tablaValores =
                    "ESTADO DEL SENSOR SEGÚN POLARIDAD\n" +
                    "-------------------------\n" +
                    "INVERTIDA:  I ~ 0 A · LED apagado (negro)\n" +
                    "CORRECTA:   I ~ 10–15 mA · LED VERDE seguro\n" +
                    "-------------------------\n" +
                    "Condición de victoria: LED verde (polaridad correcta\n" +
                    "y corriente en rango seguro, sin sobrecarga).\n\n" +
                    "PRECAUCIÓN: si crees que ya conectaste todo y una rama\n" +
                    "sigue sin encender, pide al Explorador medir esa rama\n" +
                    "en VOLTAJE: 0V = falta el cable de batería a ese riel;\n" +
                    "con voltaje pero LED apagado = falta reemplazar/voltear\n" +
                    "el LED de esa rama."
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
                    "RESISTENCIA: mismo METODO que el Reto 1\n" +
                    "(V_R = V_fuente - V_LED ; R = V_R / I_objetivo),\n" +
                    "el capacitor en paralelo no cambia esta cuenta\n" +
                    "(en regimen no consume corriente). PERO la\n" +
                    "corriente objetivo de ESTE reto es DISTINTA a\n" +
                    "la del Reto 1 — no reuses ese numero:\n\n" +
                    "CORRIENTE NOMINAL DE ESTE LED (Reto 3):\n" +
                    "  14 mA  (franja util 13,5 a 15 mA,\n" +
                    "          o sea 0,0135 A a 0,015 A)",

        objetivo  = "Corregir las 3 fallas EN ORDEN DE PRIORIDAD, igual que en Retos 1 y 2:\n" +
                    "selecciona la pieza en tu panel y pulsa ENVIAR con la polaridad/valor correcto.\n\n" +
                    "PRIORIDAD 1 -> Capacitor (humo = riesgo crítico)\n" +
                    "  Selecciona Capacitor, polaridad CORRECTA, y ENVÍA\n\n" +
                    "PRIORIDAD 2 -> LED invertido\n" +
                    "  Selecciona LED, polaridad CORRECTA, y ENVÍA\n\n" +
                    "PRIORIDAD 3 -> Resistencia incorrecta\n" +
                    "  Mide V_fuente (bornes de la bateria) y calcula\n" +
                    "  R = (V_fuente - V_LED) / I_objetivo con la\n" +
                    "  corriente nominal de ESTE reto (ver formulas,\n" +
                    "  NO la del Reto 1) y ENVIALO",

        tablaValores =
                    "RESISTENCIA CON FALLA EN LA NAVE\n" +
                    "-------------------------\n" +
                    "Medida: 2200 Ohm\n" +
                    "  Rojo-Rojo-Rojo-Oro\n" +
                    "-------------------------\n" +
                    "Usá la tabla de código de colores del Reto 1 y la\n" +
                    "corriente nominal de ESTE reto (ver formulas) para\n" +
                    "calcular el valor correcto.\n\n" +
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
            "     OJO: el riel rotulado GND de la\n" +
            "     protoboard NO esta pre-cableado al\n" +
            "     Arduino, solo une sus propios agujeros\n" +
            "     entre si (igual que cualquier fila). SI\n" +
            "     O SI hace falta un cable aparte desde\n" +
            "     ese riel (o donde termine el circuito)\n" +
            "     hasta un pin GND fisico del Arduino.\n" +
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
            "  Ves 0 mA    -> 'revisa el cable a GND' (el mas\n" +
            "                comun: falta el cable del riel\n" +
            "                GND hacia un pin GND del Arduino)\n" +
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