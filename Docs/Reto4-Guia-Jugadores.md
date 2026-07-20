# Reto 4 — Guía para Jugadores (Arduino + Protoboard)

> Guía para principiantes que **nunca han jugado** un juego cooperativo asimétrico ni
> han programado un Arduino. Lee la sección de tu rol; al final hay un glosario.

---

## 1. ¿De qué trata el Reto 4?

El equipo (2 personas) debe **hacer parpadear un LED de forma segura** usando un Arduino.
A diferencia de los retos 1–3, aquí **no hay una falla que reparar**: ustedes **diseñan el
circuito desde cero**.

Es un juego **cooperativo asimétrico**: cada quien ve y hace cosas distintas, y **solo ganan
si se comunican por voz**.

| Rol | Dónde juega | Qué ve | Qué hace |
|-----|-------------|--------|----------|
| **Técnico** | PC (monitor + teclado) | El IDE de Arduino y el monitor de telemetría (2D) | **Programa** el Arduino: escribe el código y lo "sube" |
| **Explorador** | Visor VR (Meta Quest 3) | El Arduino y la protoboard físicos en 3D | **Conecta** los cables: arma el circuito con sus manos |

La idea: el **Técnico programa un pin** y se lo dice por voz al **Explorador**, que **conecta
físicamente el LED a ese mismo pin**. Cuando el circuito está bien armado y el código sube
correctamente, el reto se valida solo.

**Meta de victoria:** el LED **parpadea** (BLINK), está conectado con una **resistencia en
serie** (≥100 Ω, recomendado 330 Ω) y el circuito **cierra a GND**.

---

## 2. Conceptos básicos (para los dos)

- **Arduino**: una placa programable. Tiene **pines** (conectores) numerados por donde sale o
  entra electricidad.
- **Pin digital**: un conector que solo tiene 2 estados: **HIGH (5 voltios = encendido)** o
  **LOW (0 voltios = apagado)**. Los pines digitales útiles van del **D2 al D13**.
- **LED**: una lucecita. Tiene polaridad: la pata **larga es el + (ánodo)** y la **corta es el
  − (cátodo)**. Si se conecta al revés, no enciende.
- **Resistencia**: limita la corriente para que el LED **no se queme**. Sin ella, el LED recibe
  demasiada corriente y se daña. Para este reto: **330 Ω**.
- **GND (tierra)**: el "retorno" de la electricidad. Todo circuito debe **cerrarse en GND**.
- **Protoboard**: la tabla con agujeritos donde el Explorador clava los cables y componentes.
- **Parpadear (BLINK)**: encender y apagar el LED en bucle (HIGH → esperar → LOW → esperar).

**El recorrido de la electricidad debe ser:**

```
Pin del Arduino  →  pata + del LED  →  pata − del LED  →  Resistencia 330Ω  →  GND
```

---

## 3. Rol TÉCNICO (PC) — Programar el Arduino

Tu trabajo es **escribir el programa (sketch)** que hace parpadear el LED y elegir **en qué pin**.

### 3.1 Abrir el IDE
1. En tu pantalla, haz **clic en el monitor del PC del Arduino**. Se abre el **IDE** (editor de
   código). Arranca en **modo Código Libre** (escribes el texto directamente).

### 3.2 ¿Cómo elijo y "veo" el número de pin?
- El Arduino tiene los pines **rotulados en la placa**: `D2, D3, D4, … D13`. El **Explorador los
  ve impresos** junto a cada agujero en su vista VR.
- **Tú eliges cualquiera entre D2 y D13** (evita D0 y D1, que son el puerto Serial RX/TX).
- **El número que escribas en el código es el pin que se activa.** Ejemplo: si escribes `7`, se
  enciende el pin **D7**.
- ⚠️ **Clave de la cooperación:** dile por voz al Explorador **qué pin elegiste** ("¡conéctalo
  al D7!"), porque él debe enchufar el LED en **ese mismo pin físico**.

### 3.3 Los comandos de Arduino (qué hace cada uno)

| Comando | Qué hace | Ejemplo |
|---------|----------|---------|
| `pinMode(pin, OUTPUT)` | Configura el pin como **salida** (para mandar corriente, p. ej. a un LED). Va en `setup()`. | `pinMode(7, OUTPUT);` |
| `pinMode(pin, INPUT)` | Configura el pin como **entrada** (para leer un sensor). *No sirve para encender un LED.* | `pinMode(7, INPUT);` |
| `digitalWrite(pin, HIGH)` | Pone el pin en **5 V → enciende** el LED. | `digitalWrite(7, HIGH);` |
| `digitalWrite(pin, LOW)` | Pone el pin en **0 V → apaga** el LED. | `digitalWrite(7, LOW);` |
| `delay(ms)` | **Espera** esa cantidad de milisegundos (1000 ms = 1 segundo). Sirve para que el parpadeo se note. | `delay(500);` |
| `analogRead(A0)` | Lee el **sensor analógico** del pin A0: devuelve un número **0–1023** (0 V→0, 5 V→1023). | `analogRead(A0);` |

Palabras clave:
- **OUTPUT** = salida · **INPUT** = entrada
- **HIGH** = encendido (5 V) · **LOW** = apagado (0 V)
- `setup()` se ejecuta **una vez** al inicio (ahí va `pinMode`).
- `loop()` se repite **para siempre** (ahí va el parpadeo).

### 3.4 El sketch completo (cópialo y cambia el pin)

```cpp
void setup() {
  pinMode(7, OUTPUT);     // D7 será una salida   (cambia 7 por tu pin)
}

void loop() {
  digitalWrite(7, HIGH);  // enciende el LED
  delay(500);             // espera 0.5 s
  digitalWrite(7, LOW);   // apaga el LED
  delay(500);             // espera 0.5 s
}                         // …y vuelve a empezar (parpadeo)
```

> El editor te da una plantilla con `__`: solo **reemplaza los `__` por tu número de pin**.
> El compilador es **tolerante** (acepta `D7`, `Pin 7`, `LED_BUILTIN`=13, mayúsculas mezcladas,
> y hasta puntos y comas faltantes), y te avisa si algo está mal.

### 3.5 Compilar y subir
1. Pulsa **COMPILAR / SUBIR** (o `Ctrl+Enter`).
2. La **consola** muestra el resultado:
   - ✅ `OK  Pin D7 | OUTPUT | BLINK ON=500ms / OFF=500ms` → todo bien.
   - ⚠️ Avisos amarillos = consejos (ej. "te falta el BLINK", "el pin está en INPUT").
   - ❌ Rojo = error (corrige y vuelve a subir).
3. `Ctrl+L` limpia la consola. El sketch **se guarda solo** entre sesiones.

### 3.6 El monitor de telemetría (HUD) — qué significa cada dato

| Indicador | Qué significa | Valor esperado al ganar |
|-----------|---------------|--------------------------|
| **V** (Voltaje) | Voltaje en el pin activo. | ~5 V cuando el pin está en HIGH |
| **I** (Corriente) | Corriente que circula, en **mA** (miliamperios). | Un valor pequeño y estable (no 0, no enorme) |
| **P** (Potencia) | Potencia consumida, en **W** (vatios). | Bajo (LED + resistencia) |
| **ADC A0** | Lectura del sensor analógico A0: **0–1023**. | Cambia según el voltaje que mide |
| **ESTADO** | Salud del circuito (texto de color): | |
| &nbsp;&nbsp;🟢 *OPERACIÓN SEGURA* | Todo bien, corriente normal. | **Este es el objetivo** |
| &nbsp;&nbsp;🔴 *CORTOCIRCUITO* | Falta la resistencia o hay un corto → peligroso. | Pide al Explorador revisar la resistencia |
| &nbsp;&nbsp;🟠 *CIRCUITO ABIERTO (0 mA)* | No pasa corriente: cable suelto o circuito sin cerrar. | Pide cerrar el circuito a GND |

> Si ves **CORTOCIRCUITO**: casi siempre falta la **resistencia en serie**.
> Si ves **CIRCUITO ABIERTO (0 mA)**: un cable está suelto o el circuito no llega a **GND**.

---

## 4. Rol EXPLORADOR (VR) — Armar el circuito

Tu trabajo es **conectar físicamente** el LED al Arduino, con tus manos, en el pin que te diga
el Técnico.

### 4.0 Cómo funciona la protoboard (léelo antes de conectar)

La protoboard **no es un solo hueco genérico**: son agujeros agrupados en **filas/rieles**, y
cada agrupación es como un cable interno ya soldado — pero **ningún riel llega solo hasta el
Arduino**. Esto confunde mucho la primera vez, así que va la regla completa:

- **Cada fila (`ROW_A`, `ROW_B`, … `ROW_F`) es su propio riel interno.** Si metes dos patitas en
  agujeros de la **misma fila**, quedan unidas eléctricamente entre sí — como si ya hubiera un
  cable soldado ahí. Filas **distintas** NO están unidas entre sí: si necesitas pasar de una fila
  a otra, hace falta un cable.
- **El riel rotulado `GND` (los agujeros `GND_D`) funciona exactamente igual que una fila
  cualquiera:** todos sus agujeros están unidos entre sí, **pero ese riel NO está pre-cableado al
  Arduino**. Verlo rotulado "GND" no significa que ya esté conectado a tierra — significa
  únicamente "estos agujeros están unidos entre ellos", igual que `ROW_B` o cualquier otra fila.
- **Por eso SIEMPRE hace falta un último cable físico**: uno que vaya desde donde termina tu
  circuito (la fila que estés usando, o el riel `GND`) hasta **uno de los pines GND que tiene el
  propio Arduino** (el Arduino trae varios marcados `GND`; sirve cualquiera). Sin ese cable final,
  el circuito queda "flotando" — nunca cierra, así que el LED no enciende aunque todo lo demás
  esté bien puesto.
- **Ventaja de usar el riel `GND` en vez de ir directo:** si tu circuito necesita cerrar en varios
  puntos, únelos todos al riel `GND` (fila `GND_D`) y luego basta **un solo cable** de ese riel al
  Arduino para cerrarlos todos a la vez — en vez de un cable por cada punto.

**Resumen en una frase:** todo riel (fila normal o el riel `GND`) es solo un punto de unión local;
el circuito no llega al Arduino hasta que un cable físico toca uno de los pines `GND` **del propio
Arduino**.

### 4.1 Pasos
1. **Escucha al Técnico:** te dirá qué pin programó (ej. "D7").
2. **Mira el Arduino** en 3D: los pines están **rotulados** (`D2`…`D13`). Ubica **el pin que te
   dijeron**.
3. **Toma el LED** de la bandeja VR (con el gatillo/grip del mando).
4. Conecta la **pata larga (+, ánodo)** del LED al **pin indicado**.
5. Toma la **resistencia de 330 Ω** y conéctala **en serie** después del LED
   (LED − → resistencia).
6. Cierra el circuito llevando el otro extremo de la resistencia a **GND**.
7. Pulsa el **botón físico de validación** (el botón VR del puesto).

### 4.2 Orden correcto de conexión

```
[Pin D7] → [LED +]  [LED −] → [Resistencia 330Ω] → [riel GND] → [cable a un pin GND del Arduino]
```

El último tramo (riel `GND` → pin `GND` del Arduino) es un **cable aparte** — no viene incluido
por estar en el riel rotulado "GND". Es el paso que más se olvida.

### 4.3 Cómo saber si va bien
- Si el Técnico ve **CORTOCIRCUITO** en su HUD → te falta la **resistencia** o está mal puesta.
- Si ve **CIRCUITO ABIERTO (0 mA)** → un **cable está suelto**, o falta el cable final desde el
  riel `GND` hasta un pin `GND` **del Arduino** (ver 4.0 — el riel por sí solo no llega al Arduino).
- Cuando todo está bien y el código está subido, al pulsar el botón de validación:
  **botón VR se pone verde + vibración (háptica) = ¡RETO COMPLETADO!**

---

## 5. Trabajo en equipo (lo más importante)

1. **Técnico** elige el pin y programa el parpadeo → **dice el pin por voz**.
2. **Explorador** conecta el LED a **ese pin**, con resistencia, cerrando a GND.
3. **Técnico** vigila el HUD y avisa: "veo cortocircuito, revisa la resistencia" / "veo 0 mA,
   revisa el cable a GND".
4. **Explorador** corrige y pulsa validar.

> El validador detecta el éxito **automáticamente** sin importar qué pin usaron, siempre que:
> **el sketch suba con BLINK**, haya **LED + resistencia ≥100 Ω** y el circuito **cierre a GND**.

---

## 6. Glosario rápido

- **Sketch**: el programa que corre en el Arduino.
- **Compilar**: revisar que el código no tenga errores antes de subirlo.
- **Subir**: enviar el programa al Arduino para que lo ejecute.
- **HIGH / LOW**: 5 V (encendido) / 0 V (apagado).
- **OUTPUT / INPUT**: pin de salida / de entrada.
- **mA**: miliamperio (medida de corriente). **W**: vatio (potencia). **V**: voltio (voltaje).
- **ADC**: convertidor analógico→digital; convierte un voltaje (0–5 V) en un número (0–1023).
- **GND**: tierra, el retorno del circuito.
- **Protoboard**: tablero de prototipos donde se clavan los componentes.
- **Háptica**: la vibración del mando VR.

---

## 7. Checklist antes de validar

**Técnico**
- [ ] Elegí un pin entre **D2 y D13** y se lo dije al Explorador.
- [ ] El sketch tiene `pinMode(pin, OUTPUT)` y el patrón **BLINK** (HIGH → delay → LOW → delay).
- [ ] La consola dice **OK** (subido sin errores).
- [ ] El HUD muestra **OPERACIÓN SEGURA** (no cortocircuito, no 0 mA).

**Explorador**
- [ ] El LED está en el **pin correcto**, con la **pata larga al +**.
- [ ] Hay una **resistencia ≥100 Ω (330 Ω)** en serie.
- [ ] El circuito **cierra a GND** — incluyendo el **cable final** desde la fila/riel que estés
      usando (aunque se llame "GND") hasta **un pin GND físico del Arduino** (ver sección 4.0).
- [ ] Presioné el **botón físico de validación**.
