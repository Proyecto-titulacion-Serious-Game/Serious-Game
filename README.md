# 🛠️ Serious Game - Electrónica VR (Tesis UDLA)

![Build](https://img.shields.io/github/actions/workflow/status/Proyecto-titulacion-Serious-Game/Serious-Game/main.yml?branch=main&label=Build%20Status)
![Docs](https://img.shields.io/badge/Docs-Doxygen-blue?logo=read-the-docs)
![Unity](https://img.shields.io/badge/Unity-6000.4.3f1-black?logo=unity)
![Linux](https://img.shields.io/badge/Runner-CachyOS-orange?logo=arch-linux)

## 📖 Descripción General
Simulador asimétrico en Realidad Virtual para la enseñanza de circuitos eléctricos. Desarrollado con **Unity 6**, Meta Quest 3, chalecos hápticos y locomoción KAT VR para una experiencia inmersiva completa.

## 🚀 Estado de los Retos de Tesis
| Reto | Descripción | Estado |
| :--- | :--- | :--- |
| **Reto 1** | Ley de Ohm y Circuitos Simples en VR. | ✅ Completado |
| **Reto 2** | Configuraciones Serie/Paralelo con multímetro. | ✅ Completado |
| **Reto 3** | Carga y descarga de capacitores con feedback háptico. | ✅ Completado |
| **Reto 4** | Integración de lógica de control con Arduino virtual. | ✅ Completado |


## 🏗️ Arquitectura de Software Detectada
*Resumen de clases core extraído automáticamente desde la documentación técnica:*
* **`AudioListenerGuard`**: Garantiza que SIEMPRE haya exactamente un AudioListener activo en la...
* **`DebugLevelSkipper`**: Salto rápido de retos con F1-F4 en Play Mode (ayuda de prueba,...
* **`FoveatedRenderingBootstrap`**: Activa Fixed Foveated Rendering (FFR) en el Explorador (Quest 3/3S...
* **`GameSettings`**: Preferencias del jugador (sensibilidad de mouse y volúmenes),...
* **`MouseGrabSimulator`**: Simula la mano VR del Explorador usando el mouse. Permite agarrar,...
* **`PerformanceBootstrap`**: Capa el framerate al arrancar (antes de cargar escena), en ambos...
* **`PerformanceLogger`**: Loguea métricas de rendimiento a la consola/Player.log cada...
* **`SceneLoader`**: Punto central para cambiar de escena. Usar desde botones UI con...
* **`TecnicoBootstrapper`**: Carga NoonA.unity de forma aditiva cuando el build del Técnico...
* **`ArduinoMonitorInteract`**: Añade al Monitor del Técnico la mecánica de click -> abre el HUD del...
* **`ClipboardZoom`**: Adjunta al clipboard del Técnico. Click -> el clipboard se acerca a...
* **`ComponentSendingTray`**: Bandeja de envío sobre la mesa del Técnico. Actúa como Mediador para...


## 🔗 Recursos y Despliegue
* 🌐 **[Documentación Técnica Online](https://proyecto-titulacion-serious-game.github.io/Serious-Game/)** (Generada por Doxygen)
* 🎮 **[Descargar Ejecutables](https://github.com/Proyecto-titulacion-Serious-Game/Serious-Game/actions)** (Sección de Artifacts)
* 📑 **[Jerarquía de Clases](https://proyecto-titulacion-serious-game.github.io/Serious-Game//inherits.html)** (Reporte visual)

---
> [!IMPORTANT]
> Este archivo se auto-genera en cada Push. Sincronizado con el código fuente y el estado del proyecto.
> **Última actualización:** 20/07/2026 21:34:18 (Quito, EC)
