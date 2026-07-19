using UnityEngine;

/// <summary>
/// Lee la caminadora KAT VR conectada por USB a la PC del Técnico (con KAT Gateway abierto) y
/// retransmite la lectura cruda al Explorador vía <see cref="GameSession.PublicarKatRemota"/>.
///
/// Caso de uso: no se quiere/puede emparejar la KAT directo al visor Quest standalone — en vez de
/// eso, la caminadora se conecta a la PC del Técnico (que ya tiene soporte KAT probado ahí) y el
/// dato de velocidad/orientación viaja por la MISMA red de Photon Fusion que ya conecta a ambos
/// roles. El PlayerController del Explorador, con <c>useKatVR=true</c> y <c>katViaRed=true</c>,
/// usa esos datos en vez de leer KATNativeSDK localmente.
///
/// Se auto-arranca (no requiere ponerlo en ninguna escena) y solo hace algo si el rol local es
/// Técnico — en el Explorador queda inactivo sin coste.
/// </summary>
public class TecnicoKatBridge : MonoBehaviour
{
    [Tooltip("Numero de serie del dispositivo. Vacío = auto-detectar.")]
    public string katSerialNumber = "";

    string _resolvedSerial = "";
    bool   _initOk;
    bool   _katBtnWasPressed;
    double _lastUpdateTime;
    float  _lastWarnTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        bool esTecnico = ConnectionManager.Instance != null &&
                          ConnectionManager.Instance.rolAutomatico == ConnectionManager.AutoConnectRole.Tecnico;
        if (!esTecnico) return;

        var go = new GameObject("[TecnicoKatBridge]");
        DontDestroyOnLoad(go);
        go.AddComponent<TecnicoKatBridge>();
    }

    void Start() => InitKat();

    void InitKat()
    {
        try
        {
            int deviceCount = KATNativeSDK.DeviceCount();
            Debug.Log($"[TecnicoKatBridge] DeviceCount = {deviceCount}");
            if (deviceCount == 0)
            {
                Debug.LogWarning("[TecnicoKatBridge] No se detectó ninguna caminadora KAT en esta PC. " +
                                 "Verifica KAT Gateway abierto y la caminadora conectada/encendida. " +
                                 "Se reintenta en el próximo Play/build.");
                return;
            }

            ResolveSerial(deviceCount);

            try { KATNativeSDK.ForceConnect(_resolvedSerial); Debug.Log($"[TecnicoKatBridge] ForceConnect('{_resolvedSerial}')"); }
            catch (System.Exception e) { Debug.LogWarning("[TecnicoKatBridge] ForceConnect falló: " + e.Message); }

            var data = KATNativeSDK.GetWalkStatus(Serial());
            if (!data.connected)
            {
                Debug.LogWarning("[TecnicoKatBridge] La caminadora aparece pero NO está 'connected'. " +
                                  "Revisa KAT Gateway y que la caminadora esté encendida.");
                return;
            }

            _initOk = true;
            Debug.Log("[TecnicoKatBridge] ✓ Caminadora KAT inicializada — retransmitiendo al Explorador por red.");
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError("[TecnicoKatBridge] No se cargó KATSDKWarpper.dll: " + e.Message);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[TecnicoKatBridge] Error inicializando KAT: " + e.Message);
        }
    }

    string Serial() => string.IsNullOrEmpty(katSerialNumber) ? _resolvedSerial : katSerialNumber;

    /// <summary>Mismo patrón que PlayerController.ResolveKatSerial: GetWalkStatus("") suele
    /// devolver datos congelados, hace falta el serial real del dispositivo tipo 1 (caminadora).</summary>
    void ResolveSerial(int deviceCount)
    {
        if (!string.IsNullOrEmpty(katSerialNumber)) { _resolvedSerial = katSerialNumber; return; }

        for (uint i = 0; i < deviceCount; i++)
        {
            try
            {
                var desc = KATNativeSDK.GetDevicesDesc(i);
                Debug.Log($"[TecnicoKatBridge] device[{i}]: name='{desc.device}' sn='{desc.serialNumber}' type={desc.deviceType} (1=caminadora, 2=tracker)");
                if (desc.deviceType == 1 && !string.IsNullOrEmpty(desc.serialNumber))
                {
                    _resolvedSerial = desc.serialNumber;
                    return;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TecnicoKatBridge] GetDevicesDesc({i}) falló: {e.Message}");
            }
        }

        try { _resolvedSerial = KATNativeSDK.GetDevicesDesc(0).serialNumber; } catch { }
        Debug.Log($"[TecnicoKatBridge] Serial fallback: '{_resolvedSerial}'");
    }

    void Update()
    {
        if (!_initOk) return;

        var gs = GameSession.Instance;
        // Solo el Host (Técnico) tiene StateAuthority sobre GameSession — sin sesión de red
        // todavía, o si por algún motivo esta PC no es el Host, no hay a quién publicarle.
        if (gs == null || gs.Object == null || !gs.Object.IsValid || !gs.Object.HasStateAuthority) return;

        KATNativeSDK.TreadMillData data;
        try { data = KATNativeSDK.GetWalkStatus(Serial()); }
        catch (System.Exception e)
        {
            if (Time.unscaledTime - _lastWarnTime > 2f)
            {
                _lastWarnTime = Time.unscaledTime;
                Debug.LogWarning("[TecnicoKatBridge] GetWalkStatus falló: " + e.Message);
            }
            return;
        }

        bool datosFrescos = data.lastUpdateTimePoint != _lastUpdateTime;
        _lastUpdateTime = data.lastUpdateTimePoint;

        bool btn = data.deviceDatas != null && data.deviceDatas.Length > 0 && data.deviceDatas[0].btnPressed;
        bool btnEdge = btn && !_katBtnWasPressed;
        _katBtnWasPressed = btn;

        gs.PublicarKatRemota(data.moveSpeed, data.bodyRotationRaw, data.connected, datosFrescos, btnEdge);
    }
}
