using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Servidor HTTP embebido que sirve el dashboard del docente.
/// Accesible desde cualquier navegador en la red LAN.
///
/// SETUP:
///   1. Añadir al mismo GO que SessionDataExporter (ej. NetworkManager).
///   2. Asignar dataExporter en el Inspector (o se busca automáticamente).
///   3. Al entrar en Play Mode, Unity imprime la URL en la consola.
///   4. El docente abre esa URL en cualquier navegador del laboratorio.
///
/// PUERTOS Y PERMISOS (Windows):
///   localhostOnly = true  → solo el PC local, sin permisos extra.
///   localhostOnly = false → toda la LAN; ejecutar Unity como admin O:
///     netsh http add urlacl url=http://+:8080/ user=Everyone
///
/// RUTAS:
///   GET  /            → Dashboard HTML
///   GET  /api/results → JSON con resultados de la última sesión
///   GET  /api/sessions→ JSON con el HISTORIAL (lista) de todas las sesiones
///   GET  /api/status  → JSON con estado actual
///   POST /api/code    → Genera un código de acceso de 4 dígitos
/// </summary>
public class DashboardServer : MonoBehaviour
{
    [Header("Configuración")]
    public int  port          = 8080;
    [Tooltip("true = solo localhost (sin permisos extra). false = toda la LAN.")]
    public bool localhostOnly = true;

    [Header("Referencias")]
    public SessionDataExporter dataExporter;

    // ─────────────────────────────────────────────
    private HttpListener  _listener;
    private Thread        _thread;
    private volatile bool _running;
    private static readonly System.Random _rng = new System.Random();

    // ─────────────────────────────────────────────
    void Start()
    {
        if (dataExporter == null)
            dataExporter = FindAnyObjectByType<SessionDataExporter>(FindObjectsInactive.Include);
        StartServer();
    }

    void OnDestroy() => StopServer();

    // ─────────────────────────────────────────────
    void StartServer()
    {
        string prefix = localhostOnly ? $"http://localhost:{port}/" : $"http://+:{port}/";
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _running = true;
            _thread = new Thread(ListenLoop) { IsBackground = true, Name = "DashboardHTTP" };
            _thread.Start();
            string ip = localhostOnly ? "localhost" : GetLocalIP();
            Debug.Log($"[DashboardServer] Panel docente en: http://{ip}:{port}/");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DashboardServer] No se pudo iniciar: {e.Message}\n" +
                             "Activa 'localhostOnly' o ejecuta Unity como administrador.");
        }
    }

    void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        _thread?.Join(500);
    }

    void ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
            catch (HttpListenerException) { break; }
            catch (Exception e) { Debug.LogWarning($"[DashboardServer] {e.Message}"); }
        }
    }

    // ─────────────────────────────────────────────
    void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            string path   = ctx.Request.Url.AbsolutePath.ToLower().TrimEnd('/');
            string method = ctx.Request.HttpMethod.ToUpper();

            if (method == "POST" && path == "/api/code")
            {
                string code = _rng.Next(1000, 10000).ToString();
                dataExporter?.SetAccessCode(code);
                Respond(ctx, 200, "application/json", "{\"code\":\"" + code + "\"}");
            }
            else if (path == "/api/results")
            {
                // JSON ya serializado en el hilo principal (JsonUtility NO se puede llamar aquí).
                Respond(ctx, 200, "application/json", dataExporter?.GetResultsJson() ?? "{}");
            }
            else if (path == "/api/live")
            {
                Respond(ctx, 200, "application/json", dataExporter?.GetLiveJson() ?? "{}");
            }
            else if (path == "/api/sessions")
            {
                Respond(ctx, 200, "application/json", dataExporter?.GetSessionsJson() ?? "{\"sessions\":[]}");
            }
            else if (path == "/api/sessions.csv")
            {
                var hist = dataExporter?.GetHistorySnapshot() ?? new SessionHistory();
                RespondCsv(ctx, BuildSessionsCsv(hist), "sesiones_tita.csv");
            }
            else if (path == "/api/records.csv")
            {
                var hist = dataExporter?.GetHistorySnapshot() ?? new SessionHistory();
                RespondCsv(ctx, BuildRecordsCsv(hist), "retos_tita.csv");
            }
            else if (path == "/api/status")
            {
                var data = dataExporter?.GetSnapshot() ?? new SessionExportData();
                string grupo = dataExporter != null ? dataExporter.grupo : "";
                string json = "{" +
                    "\"currentReto\":\"" + Escape(data.currentReto) + "\"," +
                    "\"state\":\"" + Escape(data.state) + "\"," +
                    "\"grupo\":\"" + Escape(grupo) + "\"," +
                    "\"accessCode\":\"" + Escape(data.accessCode) + "\"}";
                Respond(ctx, 200, "application/json", json);
            }
            else
            {
                Respond(ctx, 200, "text/html; charset=utf-8", DashboardHtml);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DashboardServer] Error en request: {e.Message}");
        }
    }

    static void Respond(HttpListenerContext ctx, int status, string contentType, string body)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode      = status;
            ctx.Response.ContentType     = contentType;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch { }
    }

    static string Escape(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                 .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    // ── Exportación CSV del historial ───────────────────────────────────
    static void RespondCsv(HttpListenerContext ctx, string body, string filename)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode      = 200;
            ctx.Response.ContentType     = "text/csv; charset=utf-8";
            ctx.Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{filename}\"");
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch { }
    }

    static string BuildSessionsCsv(SessionHistory h)
    {
        var sb = new StringBuilder();
        sb.Append("#,Fecha,Codigo,Score,ScoreMax,Porcentaje,Tiempo_s,Errores,Evaluacion\r\n");
        if (h?.sessions != null)
        {
            for (int i = 0; i < h.sessions.Length; i++)
            {
                var s = h.sessions[i];
                sb.Append(i + 1).Append(',')
                  .Append(Csv(s.timestamp)).Append(',')
                  .Append(Csv(s.accessCode)).Append(',')
                  .Append(s.totalScore).Append(',')
                  .Append(s.maxScore).Append(',')
                  .Append(Mathf.RoundToInt(s.scorePercent * 100f)).Append(',')
                  .Append(Mathf.RoundToInt(s.totalTimeSeconds)).Append(',')
                  .Append(s.totalErrors).Append(',')
                  .Append(Csv(s.evaluation)).Append("\r\n");
            }
        }
        return sb.ToString();
    }

    // CSV GRANULAR: una fila por (sesión, reto), con desglose de tipos de error. Para Looker/Sheets/Power BI.
    static string BuildRecordsCsv(SessionHistory h)
    {
        var sb = new StringBuilder();
        sb.Append("Sesion,Fecha,Codigo,Reto,Nota_10,Tiempo_s,Errores,Exito,Evaluacion,Tipos_error\r\n");
        if (h?.sessions != null)
        {
            for (int i = 0; i < h.sessions.Length; i++)
            {
                var s = h.sessions[i];
                if (s.records == null) continue;
                foreach (var r in s.records)
                {
                    sb.Append(i + 1).Append(',')
                      .Append(Csv(s.timestamp)).Append(',')
                      .Append(Csv(s.accessCode)).Append(',')
                      .Append(Csv(r.levelName)).Append(',')
                      .Append(r.nota.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                      .Append(Mathf.RoundToInt(r.timeSeconds)).Append(',')
                      .Append(r.errors).Append(',')
                      .Append(r.success ? 1 : 0).Append(',')
                      .Append(Csv(r.evaluation)).Append(',')
                      .Append(Csv(TypesInline(r.errorTypes))).Append("\r\n");
                }
            }
        }
        return sb.ToString();
    }

    // "cortocircuito:2;led_quemado:1" — desglose de errores en una celda.
    static string TypesInline(ErrorTagCount[] arr)
    {
        if (arr == null || arr.Length == 0) return "";
        var parts = new string[arr.Length];
        for (int i = 0; i < arr.Length; i++) parts[i] = arr[i].tipo + ":" + arr[i].count;
        return string.Join(";", parts);
    }

    // Escapa un campo CSV (comillas/comas/saltos de línea).
    static string Csv(string s)
    {
        s ??= "";
        if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    static string GetLocalIP()
    {
        try
        {
            foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "localhost";
    }

    // ─────────────────────────────────────────────
    //  HTML del dashboard — sin comillas dobles en el contenido
    //  (todas las attrs HTML y strings JS usan comilla simple)
    // ─────────────────────────────────────────────
    static readonly string DashboardHtml =
        "<!DOCTYPE html>" +
        "<html lang='es'>" +
        "<head>" +
        "<meta charset='UTF-8'>" +
        "<meta name='viewport' content='width=device-width,initial-scale=1'>" +
        "<title>TITA — Panel Docente</title>" +
        "<style>" +
        "*{box-sizing:border-box;margin:0;padding:0}" +
        "body{font-family:monospace;background:#0d1117;color:#c9d1d9;padding:24px}" +
        "h1{color:#58a6ff;margin-bottom:4px;font-size:1.6em}" +
        ".sub{color:#8b949e;font-size:.85em;margin-bottom:24px}" +
        ".grid{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:16px}" +
        "@media(max-width:600px){.grid{grid-template-columns:1fr}}" +
        ".card{background:#161b22;border:1px solid #30363d;border-radius:8px;padding:20px}" +
        ".card h2{color:#58a6ff;font-size:1em;margin-bottom:12px;text-transform:uppercase;letter-spacing:.05em}" +
        ".code-display{font-size:2.8em;color:#f0c040;letter-spacing:.4em;text-align:center;padding:12px 0;font-weight:bold}" +
        ".code-hint{font-size:.75em;color:#8b949e;text-align:center;margin-bottom:12px}" +
        ".btn{background:#21262d;color:#c9d1d9;border:1px solid #30363d;padding:8px 16px;cursor:pointer;" +
        "border-radius:6px;font-family:monospace;font-size:.9em;margin:4px 2px;transition:background .15s}" +
        ".btn:hover{background:#388bfd;color:#fff;border-color:#388bfd}" +
        ".btn-gen{background:#1f6feb;color:#fff;border-color:#1f6feb;width:100%}" +
        ".btn-gen:hover{background:#388bfd}" +
        ".stat{display:flex;justify-content:space-between;padding:6px 0;border-bottom:1px solid #21262d}" +
        ".stat:last-child{border-bottom:none}" +
        ".stat-val{color:#3fb950;font-weight:bold}" +
        ".badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:.8em}" +
        ".badge-ok{background:#1a4731;color:#3fb950}" +
        ".badge-fail{background:#4a1919;color:#f85149}" +
        ".badge-wait{background:#1c2128;color:#8b949e}" +
        "table{width:100%;border-collapse:collapse;font-size:.9em}" +
        "th{background:#21262d;padding:8px;text-align:left;font-weight:normal;color:#8b949e}" +
        "td{padding:8px;border-bottom:1px solid #21262d}" +
        "tr:last-child td{border-bottom:none}" +
        ".ok{color:#3fb950}.fail{color:#f85149}" +
        "#toast{position:fixed;bottom:20px;right:20px;background:#1f6feb;color:#fff;" +
        "padding:10px 18px;border-radius:6px;display:none;font-size:.9em}" +
        ".note{font-size:.75em;color:#8b949e;text-align:right;margin-top:8px}" +
        "</style></head><body>" +
        "<h1>TITA — Panel Docente</h1>" +
        "<p class='sub'>Serious Game — Circuitos Eléctricos VR &nbsp;|&nbsp; <span id='clock'></span></p>" +
        "<div class='card'>" +
        "  <h2>Estado de Sesión</h2>" +
        "  <div class='stat'><span>Grupo</span><span class='stat-val' id='s-grupo'>—</span></div>" +
        "  <div class='stat'><span>Reto activo</span><span class='stat-val' id='s-reto'>—</span></div>" +
        "  <div class='stat'><span>Estado</span><span id='s-state'><span class='badge badge-wait'>En espera</span></span></div>" +
        "  <p class='note'>Actualización automática cada 10 s</p>" +
        "</div>" +
        "<div class='card'>" +
        "  <h2>Sesión en Vivo — Ambos Roles</h2>" +
        "  <div class='grid'>" +
        "    <div class='stat'><span>Explorador (VR)</span><span id='l-expl'><span class='badge badge-wait'>—</span></span></div>" +
        "    <div class='stat'><span>Técnico (PC)</span><span id='l-tec'><span class='badge badge-wait'>—</span></span></div>" +
        "  </div>" +
        "  <div class='stat'><span>Reto actual</span><span class='stat-val' id='l-reto'>—</span></div>" +
        "  <div class='stat'><span>Tiempo en el reto</span><span class='stat-val' id='l-time'>0:00</span></div>" +
        "  <div class='stat'><span>Errores en el reto</span><span class='stat-val' id='l-err'>0</span></div>" +
        "  <div class='stat'><span>Retos completados</span><span class='stat-val' id='l-done'>0/4</span></div>" +
        "  <div id='l-types' style='margin-top:12px'></div>" +
        "  <div id='l-prog' style='margin-top:12px'></div>" +
        "  <p class='note'>Actualización en vivo cada 2 s</p>" +
        "</div>" +
        "<div class='card' style='margin-top:16px'>" +
        "  <h2>Resultados de Sesión (última)</h2>" +
        "  <div id='results'><p style='color:#8b949e;padding:12px 0'>Sin datos — la sesión aún no ha finalizado.</p></div>" +
        "</div>" +
        "<div class='card' style='margin-top:16px'>" +
        "  <h2>Historial de Sesiones " +
        "<a class='btn' href='/api/records.csv' download='retos_tita.csv' style='float:right;text-decoration:none;text-transform:none'>CSV por reto</a>" +
        "<a class='btn' href='/api/sessions.csv' download='sesiones_tita.csv' style='float:right;text-decoration:none;text-transform:none;margin-right:6px'>CSV por sesion</a></h2>" +
        "  <div id='sessions'><p style='color:#8b949e;padding:12px 0'>Sin sesiones registradas.</p></div>" +
        "</div>" +
        "<div id='toast'></div>" +
        "<script>" +
        "function clock(){document.getElementById('clock').textContent=new Date().toLocaleTimeString('es-EC');}" +
        "setInterval(clock,1000);clock();" +

        "async function fetchStatus(){" +
        "  try{" +
        "    var d=(await(await fetch('/api/status')).json());" +
        "    document.getElementById('s-grupo').textContent=d.grupo||'—';" +
        "    document.getElementById('s-reto').textContent=d.currentReto||'—';" +
        "    var cls=d.state==='En progreso'?'badge-ok':d.state==='Sesion finalizada'?'badge-ok':'badge-wait';" +
        "    document.getElementById('s-state').innerHTML='<span class=\\'badge '+cls+'\\'>'+d.state+'</span>';" +
        "  }catch(e){}" +
        "}" +

        "async function fetchResults(){" +
        "  try{" +
        "    var d=(await(await fetch('/api/results')).json());" +
        "    var el=document.getElementById('results');" +
        "    if(!d.hasResult){el.innerHTML='<p style=\\'color:#8b949e\\'>Sin datos — sesión no finalizada.</p>';return;}" +
        "    var s=d.summary;" +
        "    var pct=Math.round((s.scorePercent||0)*100);" +
        "    var t=fmt(s.totalTimeSeconds);" +
        "    var h='<table><thead><tr><th>Reto</th><th>Resultado</th><th>Nota /10</th><th>Tiempo</th><th>Errores</th><th>Tipos de error</th><th>Evaluación</th></tr></thead><tbody>';" +
        "    if(d.records&&d.records.length){" +
        "      for(var i=0;i<d.records.length;i++){" +
        "        var r=d.records[i];var c=r.success?'ok':'fail';var ic=r.success?'OK':'X';" +
        "        h+='<tr><td>'+r.levelName+'</td><td class=\\''+c+'\\'>'+ic+'</td><td><b>'+(r.nota!=null?r.nota.toFixed(1):'-')+'</b></td><td>'+fmt(r.timeSeconds)+'</td><td>'+r.errors+'</td><td>'+typesInline(r.errorTypes)+'</td><td>'+r.evaluation+'</td></tr>';" +
        "      }" +
        "    }" +
        "    h+='</tbody></table>';" +
        "    h+='<div style=\\'margin-top:16px;padding:12px;background:#21262d;border-radius:6px\\'>';" +
        "    h+='<b style=\\'color:#58a6ff\\'>Resumen</b> ';" +
        "    h+='Score: <b style=\\'color:#f0c040\\'>'+s.totalScore+'/'+s.maxScore+' pts ('+pct+'%)</b> | ';" +
        "    h+='Tiempo: <b>'+t+'</b> | ';" +
        "    h+='Errores: <b style=\\'color:#f85149\\'>'+s.totalErrors+'</b> | ';" +
        "    h+='<b style=\\'color:#3fb950\\'>'+(s.evaluation||'—')+'</b>';" +
        "    h+='</div>';" +
        "    if(d.timestamp)h+='<p style=\\'font-size:.75em;color:#8b949e;margin-top:8px\\'>Guardado: '+d.timestamp+'</p>';" +
        "    el.innerHTML=h;" +
        "  }catch(e){document.getElementById('results').textContent='Error al cargar resultados.';}" +
        "}" +

        "async function fetchSessions(){" +
        "  try{" +
        "    var d=(await(await fetch('/api/sessions')).json());" +
        "    var el=document.getElementById('sessions');" +
        "    if(!d.sessions||!d.sessions.length){el.innerHTML='<p style=\\'color:#8b949e\\'>Sin sesiones registradas.</p>';return;}" +
        "    var h='<table><thead><tr><th>#</th><th>Fecha</th><th>Codigo</th><th>Score</th><th>Tiempo</th><th>Errores</th><th>Evaluacion</th></tr></thead><tbody>';" +
        "    for(var i=d.sessions.length-1;i>=0;i--){" +
        "      var s=d.sessions[i];var pct=Math.round((s.scorePercent||0)*100);" +
        "      h+='<tr><td>'+(i+1)+'</td><td>'+(s.timestamp||'-')+'</td><td>'+(s.accessCode||'----')+'</td><td>'+s.totalScore+'/'+s.maxScore+' ('+pct+'%)</td><td>'+fmt(s.totalTimeSeconds)+'</td><td class=\\'fail\\'>'+s.totalErrors+'</td><td>'+(s.evaluation||'-')+'</td></tr>';" +
        "    }" +
        "    h+='</tbody></table>';" +
        "    h+='<p style=\\'font-size:.75em;color:#8b949e;margin-top:8px\\'>'+d.sessions.length+' sesiones registradas (mas recientes arriba).</p>';" +
        "    el.innerHTML=h;" +
        "  }catch(e){document.getElementById('sessions').textContent='Error al cargar sesiones.';}" +
        "}" +

        "function fmt(s){if(!s)return'0:00';var m=Math.floor(s/60);return m+':'+String(Math.floor(s%60)).padStart(2,'0');}" +

        "function typesInline(arr){" +
        "  if(!arr||!arr.length)return '—';" +
        "  var s=[];for(var i=0;i<arr.length;i++){s.push(arr[i].tipo+' ('+arr[i].count+')');}return s.join(', ');" +
        "}" +
        "function chips(arr){" +
        "  if(!arr||!arr.length)return '<span style=\\'color:#8b949e;font-size:.8em\\'>Sin errores en este reto.</span>';" +
        "  var s='<b style=\\'color:#8b949e;font-size:.8em\\'>Errores por tipo: </b>';" +
        "  for(var i=0;i<arr.length;i++){s+='<span class=\\'badge badge-fail\\' style=\\'margin:2px\\'>'+arr[i].tipo+': '+arr[i].count+'</span>';}" +
        "  return s;" +
        "}" +
        "async function fetchLive(){" +
        "  try{" +
        "    var d=(await(await fetch('/api/live')).json());" +
        "    document.getElementById('l-expl').innerHTML=d.exploradorConectado?'<span class=\\'badge badge-ok\\'>Conectado</span>':'<span class=\\'badge badge-wait\\'>Esperando</span>';" +
        "    document.getElementById('l-tec').innerHTML=d.tecnicoConectado?'<span class=\\'badge badge-ok\\'>Activo</span>':'<span class=\\'badge badge-wait\\'>Esperando</span>';" +
        "    document.getElementById('l-reto').textContent=d.currentReto||'—';" +
        "    document.getElementById('l-time').textContent=fmt(d.currentTimeSeconds);" +
        "    document.getElementById('l-err').textContent=d.currentErrors||0;" +
        "    document.getElementById('l-done').textContent=(d.retosCompletados||0)+'/4';" +
        "    document.getElementById('l-types').innerHTML=chips(d.currentErrorTypes);" +
        "    var h='';" +
        "    if(d.completedRecords&&d.completedRecords.length){" +
        "      h='<table><thead><tr><th>Reto</th><th>Resultado</th><th>Nota /10</th><th>Tiempo</th><th>Errores</th><th>Tipos</th></tr></thead><tbody>';" +
        "      for(var i=0;i<d.completedRecords.length;i++){" +
        "        var r=d.completedRecords[i];var c=r.success?'ok':'fail';var ic=r.success?'OK':'X';" +
        "        h+='<tr><td>'+r.levelName+'</td><td class=\\''+c+'\\'>'+ic+'</td><td><b>'+(r.nota!=null?r.nota.toFixed(1):'-')+'</b></td><td>'+fmt(r.timeSeconds)+'</td><td>'+r.errors+'</td><td>'+typesInline(r.errorTypes)+'</td></tr>';" +
        "      }" +
        "      h+='</tbody></table>';" +
        "    }" +
        "    document.getElementById('l-prog').innerHTML=h;" +
        "  }catch(e){}" +
        "}" +

        "function toast(msg){" +
        "  var el=document.getElementById('toast');" +
        "  el.textContent=msg;el.style.display='block';" +
        "  setTimeout(function(){el.style.display='none';},2500);" +
        "}" +

        "setInterval(function(){fetchStatus();fetchResults();fetchSessions();},10000);" +
        "setInterval(fetchLive,2000);" +
        "fetchStatus();fetchResults();fetchSessions();fetchLive();" +
        "</script></body></html>";
}
