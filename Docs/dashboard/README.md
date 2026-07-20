# Dashboard Docente — Manual de implementación

Panel web para ver las métricas de una sesión de TITA (tiempos, errores, notas por
reto) desde un navegador, sin abrir el juego. Complementa —no reemplaza— el sink a
Google Sheets/Looker Studio que ya existe.

## Arquitectura

```
Explorador (Quest/PCVR) ──red (Fusion)──▶ Técnico.exe (Host)
                                             │
                                             ├─ GameManager → PerformanceTracker → ObjectiveSystem
                                             │     (ya en Tecnico.unity, sin cambios)
                                             │
                                             ├─ DashboardServer (HTTP embebido, puerto 8080)
                                             │     "la API" — SOLO existe mientras el juego está abierto
                                             │     GET /api/status /api/live /api/results /api/sessions
                                             │     GET /api/sessions.csv /api/records.csv
                                             │     CORS ya habilitado (Access-Control-Allow-Origin: *)
                                             │
                                             └─ SessionDataExporter → Google Sheets (Apps Script)  [aparte, sin tocar]

Docs/dashboard/index.html ──CI/CD (GitHub Actions)──▶ GitHub Pages
   (frontend estático, sin backend propio)
   en el navegador: fetch(apiBase + "/api/...") → apunta al Técnico de arriba
```

No hay servidor nuevo que mantener: la API es la que ya corría (`DashboardServer.cs`,
sin cambios), y la página es estática (`index.html`, sin build step, sin dependencias).

## Requisitos

- El **Técnico** debe tener el juego en Play/ejecutando el build — la API solo
  responde mientras el proceso está vivo.
- Un navegador moderno (Chrome/Edge/Firefox) para abrir el dashboard.
- Nada que instalar del lado del profesor: la página ya vive en GitHub Pages.

La página **no tiene ninguna configuración visible** — el docente solo la abre y
ve datos. La URL de la API se resuelve sola, sin pedírsela a nadie:

1. Si el link trae `?api=...`, la usa (y la recuerda en ese navegador para la
   próxima vez, vía `localStorage`).
2. Si no, usa `http://localhost:8080` por defecto.

## Uso — caso normal (viendo desde la misma PC del Técnico)

1. Iniciar el juego del Técnico (Play en el editor, o `Tecnico.exe`). La consola
   imprime `[DashboardServer] Panel docente en: http://localhost:8080/`.
2. Abrir **https://proyecto-titulacion-serious-game.github.io/Serious-Game/dashboard/**
   en esa misma PC. No hay nada que escribir: el puntito junto a "Estado de
   sesión" se pone verde ("En vivo") solo, y las tarjetas/gráficas se llenan
   (refresco cada 2–10 s).

## Uso — viendo desde otra laptop/proyector (misma red)

Por defecto `DashboardServer.localhostOnly = true`, o sea que solo el PC del
Técnico puede pegarle a la API. Para verla desde otro dispositivo en la misma
red del laboratorio, esto lo prepara **una vez** quien arma la demo — el
docente/usuario final sigue sin ver ninguna configuración:

1. En `Assets/Scripts/Networking/DashboardBootstrap.cs`, línea `server.localhostOnly = true;`
   → cambiar a `false`, y volver a compilar/ejecutar (o ya viene así en un build
   nuevo si se edita antes de hacer build al Técnico).
2. En la PC del Técnico, correr **una vez** como administrador:
   ```
   netsh http add urlacl url=http://+:8080/ user=Everyone
   ```
   (o correr el juego como administrador esa sesión).
3. Averiguar la IP local del Técnico (`ipconfig`, algo tipo `192.168.x.x`) — la
   consola de Unity también la imprime en ese caso.
4. Compartir con el otro dispositivo el link con el parámetro ya puesto:
   `https://proyecto-titulacion-serious-game.github.io/Serious-Game/dashboard/?api=http://192.168.x.x:8080`
   Con abrir ese link una vez alcanza — el navegador lo recuerda de ahí en adelante.

**Aviso de contenido mixto (técnico, no aparece en la UI):** GitHub Pages sirve
por HTTPS; un navegador puede bloquear el `fetch` a una IP `http://` de la LAN
por política de contenido mixto (a `localhost` normalmente NO lo bloquea, por
eso el caso "misma PC" siempre funciona). Si el puntito queda en rojo al usar
el link con `?api=`, esa es la causa más probable — la alternativa más simple
es compartir pantalla o proyectar la PC del Técnico directamente.

## Despliegue (CI/CD)

El workflow `.github/workflows/main.yml` ya lo publica solo. En cada push a
`main`/`development`:

1. `Generar Doxygen` crea `docs/html/` (documentación técnica).
2. `Copiar Dashboard Docente` copia `Docs/dashboard/index.html` →
   `docs/html/dashboard/index.html`.
3. `Subir a GitHub Pages` / `Desplegar Documentación` publican todo `docs/html/`.

Para actualizar el dashboard: editar `Docs/dashboard/index.html` y hacer push —
no requiere tocar Unity ni volver a compilar el juego (la API no cambió).

## Editar la API (agregar/quitar un campo de métricas)

Los endpoints viven en `Assets/Scripts/Networking/DashboardServer.cs`
(`HandleRequest`, rutas `/api/*`). Si se agrega un campo ahí, sí requiere rebuild
del Técnico (`Tools → TITA → Build → Tecnico`) para que la API nueva quede en el
`.exe`; el HTML del dashboard se actualiza aparte y por separado en
`Docs/dashboard/index.html` (push a GitHub, sin rebuild).

## Troubleshooting

| Síntoma | Causa probable | Fix |
|---|---|---|
| Puntito rojo, "Sin conexión con el Técnico" | El juego del Técnico no está corriendo | Verificar que el juego esté en Play y que la consola haya impreso la URL |
| Conecta en la misma PC pero no desde otra | `localhostOnly = true` (default), o no se abrió el link con `?api=` en ese dispositivo | Ver sección "viendo desde otra laptop" arriba |
| Puntito rojo solo al usar el link `?api=...` con una IP de LAN | Contenido mixto (HTTPS→HTTP) bloqueado por el navegador | Ver el aviso técnico de esa misma sección; compartir pantalla como alternativa |
| Los CSV no descargan | Mismo problema de conexión que arriba — los links apuntan a `apiBase + /api/*.csv` | Confirmar que el puntito esté verde primero |
| Cambié el HTML y no se ve reflejado | El workflow de CI/CD no corrió, o el navegador cacheó la página vieja | Revisar la pestaña Actions del repo; recargar con Ctrl+Shift+R |

## Qué NO toca esto

El sink a Google Sheets (`SessionDataExporter` → Apps Script → hoja `Sesiones`/
`Retos`, ver `Docs/Metricas-y-Dashboard.md`) sigue funcionando exactamente igual,
en paralelo. Este dashboard es un canal adicional de solo lectura para ver la
sesión en vivo sin depender de Sheets/Looker Studio.
