using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Blanco.Networking
{
    public class VivoxManager : NetworkBehaviour
    {
        public static VivoxManager Instance { get; private set; }
        
        [Header("Debug")]
        public bool showDebugLogs = true;
        
        // Estados locales de mute
        private HashSet<string> mutedPlayers = new HashSet<string>();
        private bool allMuted = false;
        
        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        
        #region Public Methods (Solo Host)
        
        /// <summary>
        /// Silencia a todos los usuarios excepto a uno específico.
        /// Solo el host puede llamar este método.
        /// </summary>
        /// <param name="exceptClientId">ClientId del jugador que NO será silenciado</param>
        public void MuteAllExcept(ulong exceptClientId)
        {
            if (!IsHost)
            {
                Debug.LogWarning("⚠️ Solo el host puede silenciar jugadores");
                return;
            }
            
            if (showDebugLogs)
                Debug.Log($"🔇 Host silenciando a todos excepto cliente {exceptClientId}");
            
            // Enviar RPC a todos los clientes
            MuteAllExceptClientRpc(exceptClientId);
        }
        
        /// <summary>
        /// Silencia a todos los usuarios.
        /// Solo el host puede llamar este método.
        /// </summary>
        public void MuteAll()
        {
            if (!IsHost)
            {
                Debug.LogWarning("⚠️ Solo el host puede silenciar jugadores");
                return;
            }
            
            if (showDebugLogs)
                Debug.Log($"🔇 Host silenciando a todos los jugadores");
            
            // Enviar RPC a todos los clientes
            MuteAllClientRpc();
        }
        
        /// <summary>
        /// Desilencia a todos los usuarios.
        /// Solo el host puede llamar este método.
        /// </summary>
        public void UnmuteAll()
        {
            if (!IsHost)
            {
                Debug.LogWarning("⚠️ Solo el host puede desilenciar jugadores");
                return;
            }
            
            if (showDebugLogs)
                Debug.Log($"🔊 Host desilenciando a todos los jugadores");
            
            // Enviar RPC a todos los clientes
            UnmuteAllClientRpc();
        }
        
        #endregion
        
        #region Client RPCs
        
        [ClientRpc]
        private void MuteAllExceptClientRpc(ulong exceptClientId)
        {
            if (showDebugLogs)
                Debug.Log($"🔇 Recibido: Silenciar todos excepto cliente {exceptClientId}");
            
            // Si soy el cliente exceptuado, me desilenció (en caso de que estuviera silenciado)
            if (NetworkManager.Singleton.LocalClientId == exceptClientId)
            {
                if (showDebugLogs)
                    Debug.Log($"✅ Soy el cliente exceptuado ({exceptClientId}), me desilenció automáticamente");
                
                // Desilenciar para asegurar que puedo hablar
                UnmuteLocalMicrophone();
                return;
            }
            
            // Silenciar localmente mi micrófono
            MuteLocalMicrophone();
        }
        
        [ClientRpc]
        private void MuteAllClientRpc()
        {
            if (showDebugLogs)
                Debug.Log($"🔇 Recibido: Silenciar a todos");
            
            // Silenciar localmente mi micrófono
            MuteLocalMicrophone();
        }
        
        [ClientRpc]
        private void UnmuteAllClientRpc()
        {
            if (showDebugLogs)
                Debug.Log($"🔊 Recibido: Desilenciar a todos");
            
            // Desilenciar localmente mi micrófono
            UnmuteLocalMicrophone();
        }
        
        #endregion
        
        #region Local Audio Control
        
        /// <summary>
        /// Silencia localmente el micrófono del cliente
        /// </summary>
        private void MuteLocalMicrophone()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    VivoxService.Instance.MuteInputDevice();
                    allMuted = true;
                    
                    if (showDebugLogs)
                        Debug.Log($"🔇 Micrófono local silenciado");
                }
                else
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ VivoxService no disponible para silenciar");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al silenciar micrófono: {e.Message}");
            }
        }
        
        /// <summary>
        /// Desilencia localmente el micrófono del cliente
        /// </summary>
        private void UnmuteLocalMicrophone()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    VivoxService.Instance.UnmuteInputDevice();
                    allMuted = false;
                    mutedPlayers.Clear();
                    
                    if (showDebugLogs)
                        Debug.Log($"🔊 Micrófono local desilenciado");
                }
                else
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ VivoxService no disponible para desilenciar");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al desilenciar micrófono: {e.Message}");
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Verifica si el micrófono local está silenciado
        /// </summary>
        public bool IsLocalMicrophoneMuted()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    return VivoxService.Instance.IsInputDeviceMuted;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al verificar estado del micrófono: {e.Message}");
            }
            
            return false;
        }
        
        /// <summary>
        /// Obtiene el estado actual de mute
        /// </summary>
        public bool IsAllMuted()
        {
            return allMuted;
        }
        
        /// <summary>
        /// Toggle manual del micrófono local (para uso individual)
        /// </summary>
        public void ToggleLocalMicrophone()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    bool currentlyMuted = VivoxService.Instance.IsInputDeviceMuted;
                    
                    if (currentlyMuted)
                    {
                        VivoxService.Instance.UnmuteInputDevice();
                    }
                    else
                    {
                        VivoxService.Instance.MuteInputDevice();
                    }
                    
                    if (showDebugLogs)
                        Debug.Log($"🎤 Micrófono local: {(!currentlyMuted ? "Silenciado" : "Activado")}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al cambiar estado del micrófono: {e.Message}");
            }
        }
        
        #endregion
        
        #region Audio Device Management
        
        /// <summary>
        /// Obtiene todos los nombres de dispositivos de entrada (micrófonos) disponibles
        /// </summary>
        public List<string> GetAvailableInputDevices()
        {
            var deviceNames = new List<string>();
            
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    var availableDevices = VivoxService.Instance.AvailableInputDevices;
                    foreach (var device in availableDevices)
                    {
                        deviceNames.Add(device.DeviceName);
                    }
                    
                    if (showDebugLogs)
                        Debug.Log($"🎤 Encontrados {deviceNames.Count} dispositivos de entrada");
                }
                else
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ VivoxService no disponible para obtener dispositivos de entrada");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al obtener dispositivos de entrada: {e.Message}");
            }
            
            return deviceNames;
        }
        
        /// <summary>
        /// Obtiene todos los nombres de dispositivos de salida (auriculares/altavoces) disponibles
        /// </summary>
        public List<string> GetAvailableOutputDevices()
        {
            var deviceNames = new List<string>();
            
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    var availableDevices = VivoxService.Instance.AvailableOutputDevices;
                    foreach (var device in availableDevices)
                    {
                        deviceNames.Add(device.DeviceName);
                    }
                    
                    if (showDebugLogs)
                        Debug.Log($"🔊 Encontrados {deviceNames.Count} dispositivos de salida");
                }
                else
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ VivoxService no disponible para obtener dispositivos de salida");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al obtener dispositivos de salida: {e.Message}");
            }
            
            return deviceNames;
        }
        
        /// <summary>
        /// Obtiene el nombre del dispositivo de entrada actualmente seleccionado
        /// </summary>
        public string GetCurrentInputDevice()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    var currentDevice = VivoxService.Instance.ActiveInputDevice;
                    
                    if (currentDevice != null)
                    {
                        if (showDebugLogs)
                            Debug.Log($"🎤 Dispositivo de entrada actual: {currentDevice.DeviceName}");
                        return currentDevice.DeviceName;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al obtener dispositivo de entrada actual: {e.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Obtiene el nombre del dispositivo de salida actualmente seleccionado
        /// </summary>
        public string GetCurrentOutputDevice()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    var currentDevice = VivoxService.Instance.ActiveOutputDevice;
                    
                    if (currentDevice != null)
                    {
                        if (showDebugLogs)
                            Debug.Log($"🔊 Dispositivo de salida actual: {currentDevice.DeviceName}");
                        return currentDevice.DeviceName;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al obtener dispositivo de salida actual: {e.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Configura el dispositivo de entrada (micrófono) por nombre
        /// </summary>
        /// <param name="deviceName">Nombre del dispositivo a configurar como entrada</param>
        public async void SetInputDevice(string deviceName)
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn && !string.IsNullOrEmpty(deviceName))
                {
                    var availableDevices = VivoxService.Instance.AvailableInputDevices;
                    var targetDevice = availableDevices.FirstOrDefault(d => d.DeviceName.Equals(deviceName, System.StringComparison.OrdinalIgnoreCase));
                    
                    if (targetDevice != null)
                    {
                        await VivoxService.Instance.SetActiveInputDeviceAsync(targetDevice);
                        
                        if (showDebugLogs)
                            Debug.Log($"🎤 Dispositivo de entrada configurado: {deviceName}");
                    }
                    else
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ No se encontró dispositivo de entrada: {deviceName}");
                    }
                }
                else
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ No se pudo configurar dispositivo de entrada");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al configurar dispositivo de entrada: {e.Message}");
            }
        }
        
        /// <summary>
        /// Configura el dispositivo de salida (auriculares/altavoces) por nombre
        /// </summary>
        /// <param name="deviceName">Nombre del dispositivo a configurar como salida</param>
        public async void SetOutputDevice(string deviceName)
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn && !string.IsNullOrEmpty(deviceName))
                {
                    var availableDevices = VivoxService.Instance.AvailableOutputDevices;
                    var targetDevice = availableDevices.FirstOrDefault(d => d.DeviceName.Equals(deviceName, System.StringComparison.OrdinalIgnoreCase));
                    
                    if (targetDevice != null)
                    {
                        await VivoxService.Instance.SetActiveOutputDeviceAsync(targetDevice);
                        
                        if (showDebugLogs)
                            Debug.Log($"🔊 Dispositivo de salida configurado: {deviceName}");
                    }
                    else
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ No se encontró dispositivo de salida: {deviceName}");
                    }
                }
                else
                {
                    if (showDebugLogs)
                        Debug.LogWarning($"⚠️ No se pudo configurar dispositivo de salida");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al configurar dispositivo de salida: {e.Message}");
            }
        }
        
        /// <summary>
        /// Configura el dispositivo de entrada por DeviceID (más específico)
        /// </summary>
        /// <param name="deviceId">ID del dispositivo a configurar como entrada</param>
        public async void SetInputDeviceById(string deviceId)
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn && !string.IsNullOrEmpty(deviceId))
                {
                    var availableDevices = VivoxService.Instance.AvailableInputDevices;
                    var targetDevice = availableDevices.FirstOrDefault(d => d.DeviceID.Equals(deviceId, System.StringComparison.OrdinalIgnoreCase));
                    
                    if (targetDevice != null)
                    {
                        await VivoxService.Instance.SetActiveInputDeviceAsync(targetDevice);
                        
                        if (showDebugLogs)
                            Debug.Log($"🎤 Dispositivo de entrada configurado por ID: {deviceId} ({targetDevice.DeviceName})");
                    }
                    else
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ No se encontró dispositivo de entrada con ID: {deviceId}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al configurar dispositivo de entrada por ID: {e.Message}");
            }
        }
        
        /// <summary>
        /// Configura el dispositivo de salida por DeviceID (más específico)
        /// </summary>
        /// <param name="deviceId">ID del dispositivo a configurar como salida</param>
        public async void SetOutputDeviceById(string deviceId)
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn && !string.IsNullOrEmpty(deviceId))
                {
                    var availableDevices = VivoxService.Instance.AvailableOutputDevices;
                    var targetDevice = availableDevices.FirstOrDefault(d => d.DeviceID.Equals(deviceId, System.StringComparison.OrdinalIgnoreCase));
                    
                    if (targetDevice != null)
                    {
                        await VivoxService.Instance.SetActiveOutputDeviceAsync(targetDevice);
                        
                        if (showDebugLogs)
                            Debug.Log($"🔊 Dispositivo de salida configurado por ID: {deviceId} ({targetDevice.DeviceName})");
                    }
                    else
                    {
                        if (showDebugLogs)
                            Debug.LogWarning($"⚠️ No se encontró dispositivo de salida con ID: {deviceId}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al configurar dispositivo de salida por ID: {e.Message}");
            }
        }
        
        /// <summary>
        /// Obtiene información detallada de todos los dispositivos disponibles
        /// </summary>
        public void LogAllAvailableDevices()
        {
            try
            {
                if (VivoxService.Instance != null && VivoxService.Instance.IsLoggedIn)
                {
                    Debug.Log("🎤 === DISPOSITIVOS DE ENTRADA ===");
                    var inputDevices = VivoxService.Instance.AvailableInputDevices;
                    var currentInput = VivoxService.Instance.ActiveInputDevice;
                    
                    for (int i = 0; i < inputDevices.Count; i++)
                    {
                        var device = inputDevices[i];
                        bool isCurrent = currentInput != null && currentInput.DeviceID == device.DeviceID;
                        Debug.Log($"  [{i}] {device.DeviceName} (ID: {device.DeviceID}) {(isCurrent ? "(ACTUAL)" : "")}");
                    }
                    
                    Debug.Log("🔊 === DISPOSITIVOS DE SALIDA ===");
                    var outputDevices = VivoxService.Instance.AvailableOutputDevices;
                    var currentOutput = VivoxService.Instance.ActiveOutputDevice;
                    
                    for (int i = 0; i < outputDevices.Count; i++)
                    {
                        var device = outputDevices[i];
                        bool isCurrent = currentOutput != null && currentOutput.DeviceID == device.DeviceID;
                        Debug.Log($"  [{i}] {device.DeviceName} (ID: {device.DeviceID}) {(isCurrent ? "(ACTUAL)" : "")}");
                    }
                }
                else
                {
                    Debug.LogWarning("⚠️ VivoxService no disponible para listar dispositivos");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error al listar dispositivos: {e.Message}");
            }
        }
        
        /// <summary>
        /// Guarda las preferencias de dispositivos en PlayerPrefs
        /// </summary>
        public void SaveDevicePreferences()
        {
            var currentInput = GetCurrentInputDevice();
            var currentOutput = GetCurrentOutputDevice();
            
            if (!string.IsNullOrEmpty(currentInput))
            {
                PlayerPrefs.SetString("VivoxInputDevice", currentInput);
                if (showDebugLogs)
                    Debug.Log($"💾 Guardado dispositivo de entrada: {currentInput}");
            }
            
            if (!string.IsNullOrEmpty(currentOutput))
            {
                PlayerPrefs.SetString("VivoxOutputDevice", currentOutput);
                if (showDebugLogs)
                    Debug.Log($"💾 Guardado dispositivo de salida: {currentOutput}");
            }
            
            PlayerPrefs.Save();
        }
        
        /// <summary>
        /// Carga las preferencias de dispositivos desde PlayerPrefs
        /// </summary>
        public void LoadDevicePreferences()
        {
            string savedInputDevice = PlayerPrefs.GetString("VivoxInputDevice", "");
            string savedOutputDevice = PlayerPrefs.GetString("VivoxOutputDevice", "");
            
            if (!string.IsNullOrEmpty(savedInputDevice))
            {
                SetInputDevice(savedInputDevice);
                if (showDebugLogs)
                    Debug.Log($"📂 Cargado dispositivo de entrada: {savedInputDevice}");
            }
            
            if (!string.IsNullOrEmpty(savedOutputDevice))
            {
                SetOutputDevice(savedOutputDevice);
                if (showDebugLogs)
                    Debug.Log($"📂 Cargado dispositivo de salida: {savedOutputDevice}");
            }
        }
        
        #endregion
        
        #region Cleanup
        
        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            
            base.OnDestroy();
        }
        
        #endregion
    }
}