using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Gestor de Telemetría para envío de métricas a Supabase (PostgreSQL Cloud).
/// Maneja la validación de códigos de sesión (Dashboard) y el envío de resultados.
/// </summary>
public class AnalyticsManager : MonoBehaviour
{
    public static AnalyticsManager Instance { get; private set; }

    [Header("Configuración de Supabase")]
    public string supabaseUrlTelemetria = "https://duigabepvjpzmqllryiw.supabase.co/rest/v1/telemetria_estudiantes";
    public string supabaseUrlSesiones = "https://duigabepvjpzmqllryiw.supabase.co/rest/v1/sesiones_config";
    
    [Tooltip("La API Key provista por Supabase")]
    public string supabaseAnonKey = "sb_publishable_HHNr73IdlS1Ew5IVA6yDRg_lR35ZmT_"; 

    // Bandera que avisa a otros scripts si hay una petición HTTP en curso
    public bool SubidaEnCurso { get; private set; } = false;

    // Variables para almacenar la sesión validada desde el Dashboard
    public string idSesionActual = null;
    public string nombreClaseActual = "Modo Práctica Libre";

    // ─────────────────────────────────────────────
    // Estructura de Datos (Coincide 1:1 con tu esquema SQL)
    // ─────────────────────────────────────────────
    [Serializable]
    public class TelemetriaPayload
    {
        public string sesion_id; // UUID real de la sesión (se obtiene al validar el código)
        public string nombre_clase;
        public string grupo_estudiantes;
        public int reto_id;
        
        // Métricas de Desempeño General
        public int tiempo_resolucion_seg;
        public bool completado;
        public float nota_autograder;
        
        // Métricas de Diagnóstico Teórico (Simulador MNA / Leyes)
        public int cant_cortocircuitos;
        public int cant_sobrecorriente;
        public int cant_polaridad_invertida;
        
        // Métricas Reto 4 (Sandbox / Arduino)
        public int fallos_compilacion_ide;
        public int desconexiones_logica_fisica;
        
        // Métricas de Colaboración Asimétrica
        public int rechazos_componentes;
    }

    // ─────────────────────────────────────────────
    // Clases auxiliares para leer la respuesta GET
    // ─────────────────────────────────────────────
    [Serializable]
    public class SesionData 
    {
        public string id;
        public string nombre_clase;
    }

    [Serializable]
    public class SesionWrapper 
    {
        public SesionData[] array;
    }

    void Awake()
    {
        // Configuración Singleton para persistencia entre escenas
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Valida un código de sesión ingresado por los estudiantes (Petición GET).
    /// </summary>
    public void ValidarCodigoSesion(string codigoIngresado)
    {
        StartCoroutine(BuscarSesionRoutine(codigoIngresado));
    }

    private IEnumerator BuscarSesionRoutine(string codigo)
    {
        // Hacemos un GET filtrando por el codigo_sesion
        string urlConsulta = $"{supabaseUrlSesiones}?codigo_sesion=eq.{codigo}&select=id,nombre_clase";
        
        using (UnityWebRequest request = UnityWebRequest.Get(urlConsulta))
        {
            request.SetRequestHeader("apikey", supabaseAnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseAnonKey);
            // Sin timeout explícito, UnityWebRequest puede quedar esperando indefinidamente si
            // Supabase no responde (wifi del aula caída, DNS lento, etc.) — 8s es margen de sobra
            // para una consulta GET simple y evita que una sesión sin internet quede "cargando".
            request.timeout = 8;

            Debug.Log($"[AnalyticsManager] Buscando sesión con código: {codigo}...");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Supabase devuelve un array JSON. Lo envolvemos para que Unity pueda leerlo nativamente.
                string jsonResponse = "{\"array\":" + request.downloadHandler.text + "}";
                SesionWrapper wrapper = JsonUtility.FromJson<SesionWrapper>(jsonResponse);

                if (wrapper != null && wrapper.array != null && wrapper.array.Length > 0)
                {
                    // ¡Código encontrado! Guardamos el UUID y el nombre en memoria
                    idSesionActual = wrapper.array[0].id;
                    nombreClaseActual = wrapper.array[0].nombre_clase;
                    Debug.Log($"[AnalyticsManager] ✅ Sesión enlazada exitosamente: {nombreClaseActual} (ID: {idSesionActual})");
                }
                else
                {
                    Debug.LogWarning("[AnalyticsManager] ⚠️ El código de sesión no existe en la base de datos o está mal escrito.");
                    idSesionActual = null;
                }
            }
            else
            {
                Debug.LogError($"[AnalyticsManager] ❌ Error conectando para validar sesión: {request.error}");
            }
        }
    }

    /// <summary>
    /// Método público para enviar el payload de telemetría a Supabase (Petición POST).
    /// </summary>
    public void EnviarMetricas(TelemetriaPayload payload)
    {
        StartCoroutine(PostTelemetriaRoutine(payload));
    }

    private IEnumerator PostTelemetriaRoutine(TelemetriaPayload payload)
    {
        SubidaEnCurso = true;

        // 1. Convertir la estructura C# a formato JSON nativo
        string jsonDatos = JsonUtility.ToJson(payload);

        // BUG REAL detectado 2026-07-25 (FullPlaythroughSupabaseSend, 4/4 envíos con HTTP 400):
        // JsonUtility no sabe representar un string C# null como JSON null — lo serializa como
        // "" (cadena vacía). sesion_id es una columna 'uuid' en Postgres: "" no es un UUID válido
        // NI equivale a NULL, así que Postgres rechaza el INSERT con 22P02 "invalid input syntax
        // for type uuid". Esto rompía el envío COMPLETO (los 4 retos) de cualquier partida jugada
        // sin validar un código de sesión (el modo "Práctica Libre" que el propio
        // nombreClaseActual contempla por defecto). Se repara el JSON después de serializar en
        // vez de tocar el tipo del campo, que JsonUtility no permite anotar como opcional.
        if (string.IsNullOrEmpty(payload.sesion_id))
            jsonDatos = jsonDatos.Replace("\"sesion_id\":\"\"", "\"sesion_id\":null");

        // 2. Preparar el Request HTTP POST apuntando a la tabla de telemetría
        using (UnityWebRequest request = new UnityWebRequest(supabaseUrlTelemetria, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonDatos);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            
            // 3. Inyectar las cabeceras requeridas por Supabase (PostgREST)
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", supabaseAnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + supabaseAnonKey);
            
            // PreferHeader "return=minimal" evita que Supabase devuelva la fila insertada entera (ahorra recursos)
            request.SetRequestHeader("Prefer", "return=minimal");
            // Mismo motivo que en BuscarSesionRoutine: nunca dejar SubidaEnCurso trabado en true
            // si la red del aula se cae a mitad de un reto — el envío es fire-and-forget, no debe
            // poder demorar (ni bloquear) el siguiente reto.
            request.timeout = 8;

            Debug.Log("[AnalyticsManager] Enviando paquete de telemetría a la nube...");

            // 4. Disparar el envío de forma asíncrona
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[AnalyticsManager] ✅ Telemetría registrada exitosamente en PostgreSQL.");
            }
            else
            {
                Debug.LogError($"[AnalyticsManager] ❌ Error en el envío: {request.error}\nDetalles: {request.downloadHandler.text}");
            }
        }

        SubidaEnCurso = false;
    }
}